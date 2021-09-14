using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace Dotnet6Sqlite
{
    public class TestContext : DbContext
    {
        const string dbPath = "./TestDB.db";

        public DbSet<Folder> Folders { get; set; }
        public DbSet<Image> Images { get; set; }
        public DbSet<ImageMetaData> ImageMetaData { get; set; }
        public DbSet<Tag> Tags { get; set; }
        public DbSet<ImageTag> ImageTags { get; set; }
        public DbSet<Basket> Baskets { get; set; }
        public DbSet<BasketEntry> BasketEntries { get; set; }

        /// <summary>
        /// Basic initialisation for the DB that are generic to all DB types
        /// </summary>
        /// <param name="options"></param>
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            string dataSource = $"Data Source={dbPath}";
            options.UseSqlite(dataSource);
        }

        /// <summary>
        /// Called when the model is created by EF, this describes the key
        /// relationships between the objects
        /// </summary>
        /// <param name="modelBuilder"></param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Many to many via ImageTags
            var it = modelBuilder.Entity<ImageTag>();
            it.HasKey(x => new { x.ImageId, x.TagId });

            it.HasOne(p => p.Image)
                .WithMany(p => p.ImageTags)
                .HasForeignKey(p => p.ImageId)
                .OnDelete(DeleteBehavior.Cascade);

            it.HasOne(p => p.Tag)
                .WithMany(p => p.ImageTags)
                .HasForeignKey(p => p.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<BasketEntry>()
                .HasOne(a => a.Image)
                .WithMany(b => b.BasketEntries);

            modelBuilder.Entity<Image>()
                .HasOne(img => img.MetaData)
                .WithOne(meta => meta.Image)
                .HasForeignKey<ImageMetaData>(i => i.ImageId);

            modelBuilder.Entity<ImageTag>().HasIndex(x => new { x.ImageId, x.TagId }).IsUnique();
            modelBuilder.Entity<Image>().HasIndex(x => new { x.FolderId });

            modelBuilder.Entity<ImageMetaData>().HasIndex(x => x.ImageId);
            modelBuilder.Entity<BasketEntry>().HasIndex(x => new { x.ImageId, x.BasketId }).IsUnique();
        }
    }

    public class Folder
    {
        [Key]
        public int FolderId { get; set; }
    }

    public class Image
    {
        [Key]
        public int ImageId { get; set; }

        public int FolderId { get; set; }
        public virtual Folder Folder { get; set; }

        public virtual ImageMetaData MetaData { get; set; }
        // An image can appear in many baskets
        public virtual List<BasketEntry> BasketEntries { get; } = new List<BasketEntry>();
        // An image can have many tags
        public virtual List<ImageTag> ImageTags { get; }
    }

    public class ImageMetaData
    {
        [Key]
        public int MetaDataId { get; set; }

        [Required]
        public virtual Image Image { get; set; }
        public int ImageId { get; set; }
    }

    public class Tag
    {
        [Key]
        public int TagId { get; set; }

        public virtual List<ImageTag> ImageTags { get; } = new List<ImageTag>();
    }

    public class ImageTag
    {
        [Key]
        public int ImageId { get; set; }
        public virtual Image Image { get; set; }

        [Key]
        public int TagId { get; set; }
        public virtual Tag Tag { get; set; }

    }

    public class Basket
    {
        [Key]
        public int BasketId { get; set; }

        public virtual List<BasketEntry> BasketEntries { get; } = new List<BasketEntry>();
    }

    public class BasketEntry
    {
        [Key]
        public int BasketEntryId { get; set; }

        [Required]
        public virtual Image Image { get; set; }
        public int ImageId { get; set; }

        [Required]
        public virtual Basket Basket { get; set; }
        public int BasketId { get; set; }
    }
}
