# EFCore6TableJoinBug
This project is a small repro that demonstrates the EFCore issue [17622](https://github.com/dotnet/efcore/issues/17622) 
(also [19418](https://github.com/dotnet/efcore/issues/19418)).

The project includes the following data model:

* `Image` - contains a list of `ImageTags`
* `ImageTag` - foreign key join between `Image` and `Tag`
* `Tag` - has a primary key
* `BasketEntries` is a single foreign-key relation to `Image`.

This gives me a many-to-many relationship between image and tag, via ImageTag. Since BasketEntry and Image are 
one-to-one, starting with db.BasketEntries acts as a filter (there is only one record in BasketEntries).

The performance characteristics are the issue - I noticed that when I get to 500k images and 1.2m ImageTag 
records, the above code takes more than 3 seconds to pull back the single record from BasketEntries, and hence 
a single image (and around 10 related imageTags and tags).

# Running the Test

The repro will:

1. Check if the DB exists and if the test data has been created. 
2. If there's no existing test data, it'll be created. This will take some time, as 500,000 images and 1,500,000 
   imagetag records need to be created.
3. It'll then run two Linq/EFCore queries, the problematic one, and an optimised query which returns the same results.

The expected output is something like this (on my M1 MacBook Pro):

```
About to create SQLite DB...
Successfully created DB...
Creating 500000 images + metadata.
Creating 10000 Tags.
Creating 3 ImageTags for every image.
..................................................
Creating 5 Baskets.
Creating 15 Basket Entries.
Test Data created.
Running query with bug...
 Loaded 15 images, with 45 tags.
 Loaded 15 images, with 45 tags.
 Loaded 15 images, with 45 tags.
 Loaded 15 images, with 45 tags.
 Loaded 15 images, with 45 tags.
Bug Query run 5x in 7281ms (1456ms per run).
Running fixed query...
 Loaded 15 images, with 45 tags.
 Loaded 15 images, with 45 tags.
 Loaded 15 images, with 45 tags.
 Loaded 15 images, with 45 tags.
 Loaded 15 images, with 45 tags.
Fixed Query run 5x in 95ms (19ms per run).
```

You can see that by eagerly loading the data, the unfiltered inner join loads the entire `ImageTags` table 
(1.5m records) which is extremely slow - taking 1.5s per run. Doing the filtered join results in the query 
running 99% faster.

The fix for this issue is to change the EFCore SQL generation to not use an unfiltered select/join on the
query. So instead of the query:

```
SELECT "b"."ImageId", "i"."ImageId",
FROM "BasketEntries" AS "b"
INNER JOIN "Images" AS "i" ON "b"."ImageId" = "i"."ImageId"
LEFT JOIN (
   SELECT "i0"."ImageId", "i0"."TagId", "t"."TagId" AS "TagId0", "t"."Keyword"
   FROM "ImageTags" AS "i0"
   INNER JOIN "Tags" AS "t" ON "i0"."TagId" = "t"."TagId"
) AS "t0" ON "i"."ImageId" = "t0"."ImageId"
ORDER BY "b"."ImageId", "i"."ImageId", "t0"."ImageId", "t0"."TagId", "t0"."TagId0"
```
the generated SQL should be something like:
```
SELECT "b"."ImageId", "i"."ImageId"
FROM "BasketEntries" AS "b"
INNER JOIN "Images" AS "i" ON "b"."ImageId" = "i"."ImageId"
LEFT JOIN "ImageTags" AS "i0" ON "i"."ImageId" = "i0"."ImageId"
LEFT JOIN "Tags" as "t" on "i0".tagId = t.TagID
ORDER BY "b"."ImageId", "i"."ImageId", "i0"."ImageId", "i0"."TagId"
```
This would likely run even faster than the optimised query above, so could complete in sub-10ms.
