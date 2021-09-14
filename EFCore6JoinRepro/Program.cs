using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Dotnet6Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EFCore6JoinRepro
{
    class Program
    {
        static Random rnd = new Random();
        static int basketEntryCount = 15;

        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("About to create SQLite DB...");

                using var db = new TestContext();

                if (db.BasketEntries.Count() != basketEntryCount)
                {
                    db.Database.EnsureDeleted();
                    db.Database.EnsureCreated();

                    Console.WriteLine("Successfully created DB...");

                    GenerateTestData(500000);
                }

                RunTestQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception creating DB: {ex.Message}");
                throw;
            }
        }

        private static void RunTestQuery()
        {
            // Now the test query
            using var db = new TestContext();

            Console.WriteLine("Running query with bug...");

            Stopwatch watch = new Stopwatch();

            watch.Start();

            for (int i = 0; i < 5; i++)
            {
                var images = db.BasketEntries
                           .Include(x => x.Image)
                           .ThenInclude(x => x.ImageTags)
                           .ThenInclude(x => x.Tag)
                           .Select( x => x.Image )
                           .ToArray();

                Console.WriteLine($" Loaded {images.Count()} images, with {images.SelectMany(x => x.ImageTags).Count()} tags.");
            }

            watch.Stop();

            Console.WriteLine($"Bug Query run 5x in {watch.ElapsedMilliseconds}ms ({watch.ElapsedMilliseconds/5}ms per run).");

            watch.Reset();

            Console.WriteLine("Running fixed query...");

            watch.Start();

            for (int i = 0; i < 5; i++)
            {
                var images = db.BasketEntries
                     .Include(x => x.Image)
                     .Select(x => x.Image)
                     .ToArray();

                foreach (var img in images )
                {
                    db.Entry(img)
                        .Collection(e => e.ImageTags)
                        .Query()
                        .Include(e => e.Tag)
                        .Load();
                }

                Console.WriteLine($" Loaded {images.Count()} images, with {images.SelectMany(x => x.ImageTags).Count()} tags.");
            }

            watch.Stop();

            Console.WriteLine($"Fixed Query run 5x in {watch.ElapsedMilliseconds}ms ({watch.ElapsedMilliseconds / 5}ms per run).");

        }

        public static void GenerateTestData(int imageCount)
        {
            int tagCount = imageCount / 50;
            int folderCount = imageCount / 20;
            int basketCount = 5;

            using var db = new TestContext();

            Console.WriteLine($"Creating {folderCount} folders.");
            for (int i = 0; i < folderCount; i++)
            {
                db.Add(new Folder());
            }
            db.SaveChanges();

            Console.WriteLine($"Creating {imageCount} images + metadata.");
            for (int i = 1; i <= imageCount; i++)
            {
                var img = new Image { FolderId = rnd.Next(1, folderCount) };
                db.Add(img);
                db.Add(new ImageMetaData { Image = img });
            }
            db.SaveChanges();

            Console.WriteLine($"Creating {tagCount} Tags.");
            for (int i = 0; i < tagCount; i++)
            {
                db.Add(new Tag());
            }
            db.SaveChanges();

            Console.WriteLine($"Creating 3 ImageTags for every image.");
            for (int imageId = 1; imageId <= imageCount; imageId++)
            {
                var randomTags = RandomUniqueNums(1, tagCount, 3);

                randomTags.ForEach(x => db.Add(new ImageTag { ImageId = imageId, TagId = x }));

                if (imageId % 10000 == 0)
                {
                    db.SaveChanges();
                    Console.Write(".");
                }
            }

            Console.WriteLine();
            db.SaveChanges();

            Console.WriteLine($"Creating {basketCount} Baskets.");
            for (int i = 0; i < basketCount; i++)
            {
                db.Add(new Basket());
            }
            db.SaveChanges();

            Console.WriteLine($"Creating {basketEntryCount} Basket Entries.");
            for (int i = 0; i < basketEntryCount; i++)
            {
                int basketId = rnd.Next(1, basketCount);
                int imageId = rnd.Next(1, imageCount);
                db.Add(new BasketEntry { ImageId = imageId, BasketId = basketId });
            }
            db.SaveChanges();

            Console.WriteLine("Test Data created.");
        }

        private static List<int> RandomUniqueNums(int min, int max, int count)
        {
            var results = new List<int>();

            while (true)
            {
                int next = rnd.Next(min, max);
                if (results.Contains(next))
                    continue;

                results.Add(next);

                if (results.Count == count)
                    break;
            }

            return results;
        }
    }
}
