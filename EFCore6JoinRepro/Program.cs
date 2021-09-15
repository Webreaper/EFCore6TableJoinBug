using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Dotnet6Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EFCore6JoinRepro
{
    class Program
    {
        static Random rnd = new Random();
        static int basketEntryCount = 15;

        const string s_queryFast = @"SELECT 'i'.'ImageId', 't'.'TagId'
                                    FROM 'BasketEntries' AS 'b' 
                                    INNER JOIN 'Images' AS 'i' ON 'b'.'ImageId' = 'i'.'ImageId' 
                                    INNER JOIN 'ImageTags' AS 'it' ON 'i'.'ImageId' = 'it'.'ImageId' 
                                    INNER JOIN 'Tags' as 't' on 'it'.tagId = t.TagID 
                                    ORDER BY 'b'.'ImageId', 'i'.'ImageId', 'it'.'ImageId', 'it'.'TagId'";

        const string s_efCoreQuery = @"SELECT 'b'.'ImageId', 't0'.'TagId'
                                        FROM 'BasketEntries' AS 'b'
                                        INNER JOIN 'Images' AS 'i' ON 'b'.'ImageId' = 'i'.'ImageId'
                                        LEFT JOIN(
                                           SELECT 'i0'.'ImageId', 'i0'.'TagId', 't'.'TagId' AS 'TagId0'
                                           FROM 'ImageTags' AS 'i0'
                                           INNER JOIN 'Tags' AS 't' ON 'i0'.'TagId' = 't'.'TagId'
                                        ) AS 't0' ON 'i'.'ImageId' = 't0'.'ImageId'
                                        ORDER BY 'b'.'ImageId', 'i'.'ImageId', 't0'.'ImageId', 't0'.'TagId', 't0'.'TagId0'";

        private static bool TestDataExists()
        {
            try
            {
                using var db = new TestContext();

                return db.BasketEntries.Count() == basketEntryCount;
            }
            catch
            {
                return false;
            }
        }

        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("About to create SQLite DB...");

                using var db = new TestContext();

                if (! TestDataExists())
                {
                    db.Database.EnsureDeleted();
                    db.Database.EnsureCreated();

                    Console.WriteLine("Successfully created DB...");

                    GenerateTestData(500000);
                }
                else
                    Console.WriteLine("Test data already exists.");

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

            Console.WriteLine("Running standard EFCore query with bug...");

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

                Console.WriteLine($" Loaded {images.Count()} images.");
            }

            watch.Stop();

            Console.WriteLine($"Bug Query run 5x in {watch.ElapsedMilliseconds}ms ({watch.ElapsedMilliseconds/5}ms per run).\n");

            Console.WriteLine("Running Two-part optimized Linq query with Load...");
            watch.Reset();
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

                Console.WriteLine($" Loaded {images.Count()} images");
            }

            watch.Stop();
            Console.WriteLine($"Fixed Linq query run 5x in {watch.ElapsedMilliseconds}ms ({watch.ElapsedMilliseconds / 5}ms per run).\n");

            Console.WriteLine("Running EFCore-generated query using FromSqlRaw...");
            watch.Reset();
            watch.Start();

            for (int i = 0; i < 5; i++)
            {
                var sqlImages = db.Images.FromSqlRaw(s_efCoreQuery).ToList();
                // NOTE: I'm not sure why sqlImages.Count is 45 here. There are 45 rows returned
                // but 15 distinct images (which is correct). I'm guessing it's something to do
                // with the way the images are converted into the DbSet, not 'distinct'ing them.
                // No time to fix/investigate. The same happens for both SQLRaw queries, this one
                // the the one below.
                Console.WriteLine($" Loaded {sqlImages.Count()} images");
            }

            watch.Stop();
            Console.WriteLine($"EFCore SQL Query run 5x in {watch.ElapsedMilliseconds}ms ({watch.ElapsedMilliseconds / 5}ms per run).\n");

            Console.WriteLine("Running optimised inner-join query using FromSqlRaw...");
            watch.Reset();
            watch.Start();

            for (int i = 0; i < 5; i++)
            {
                var sqlImages = db.Images.FromSqlRaw(s_queryFast).ToList();
                Console.WriteLine($" Loaded {sqlImages.Count()} images");
            }

            watch.Stop();
            Console.WriteLine($"SQL Query run 5x in {watch.ElapsedMilliseconds}ms ({watch.ElapsedMilliseconds / 5}ms per run).\n");
        }

        public static void GenerateTestData(int imageCount)
        {
            int tagCount = imageCount / 50;
            int folderCount = imageCount / 20;
            int basketCount = 5;

            using var db = new TestContext();

            Console.WriteLine($"Creating {imageCount} images.");
            for (int i = 1; i <= imageCount; i++)
            {
                db.Add(new Image() );
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
