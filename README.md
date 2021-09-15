# EFCore6TableJoinBug
This project is a small repro that demonstrates the EFCore issue [17622](https://github.com/dotnet/efcore/issues/17622) 
(also [19418](https://github.com/dotnet/efcore/issues/19418)).

The project includes the following data model:

* `Image` - contains a list of `ImageTags`
* `ImageTag` - foreign key join between `Image` and `Tag`
* `Tag` - has a primary key
* `BasketEntries` is a single foreign-key relation to `Image`.

This gives me a many-to-many relationship between image and tag, via ImageTag. Since BasketEntry and Image are 
one-to-one, starting with db.BasketEntries acts as a filter (there is only a handful of records in BasketEntries).

The performance characteristics are the issue - I noticed that when I get to 500k images and 1.2m ImageTag 
records, the above code takes more than 3 seconds to pull back the single record from BasketEntries, and hence 
a single image (and around 10 related imageTags and tags).

# Running the Test

The repro will:

1. Check if the DB exists and if the test data has been created. 
2. If there's no existing test data, it'll be created. This will take some time, as 500,000 images and 1,500,000 
   imagetag records need to be created.
3. It'll then run two Linq/EFCore queries [here](https://github.com/Webreaper/EFCore6TableJoinBug/blob/main/EFCore6JoinRepro/Program.cs#L55)
   and [here](https://github.com/Webreaper/EFCore6TableJoinBug/blob/main/EFCore6JoinRepro/Program.cs#L77), the 
   problematic one, and an optimised query which returns the same results by executing a `Load` to pull in the filtered ImageTags data.
4. It will then run the generated two querys in 'How this should be fixed' section below, using `FromSqlRaw`. 
 
NOTE: that when #4 is run, for some reason the returned image count is 45, not 15 - due to the fact that 3 image rows
are returned (one for each tag). There's probably a way to make it return 15 images each with 3 tags, but I'm new to 
`FromSqlRaw`... but the result is the same with the EFCore-generated query and my optimised one, so meh. :)

The expected output is something like this (on my M1 MacBook Pro):

```
About to create SQLite DB...
Test data already exists.
Running standard EFCore query with bug...
 Loaded 15 images.
 Loaded 15 images.
 Loaded 15 images.
 Loaded 15 images.
 Loaded 15 images.
Bug Query run 5x in 8243ms (1648ms per run).

Running Two-part optimized Linq query with Load...
 Loaded 15 images
 Loaded 15 images
 Loaded 15 images
 Loaded 15 images
 Loaded 15 images
Fixed Linq query run 5x in 1883ms (376ms per run).

Running EFCore-generated query using FromSqlRaw...
 Loaded 45 images
 Loaded 45 images
 Loaded 45 images
 Loaded 45 images
 Loaded 45 images
EFCore SQL Query run 5x in 7773ms (1554ms per run).

Running optimised inner-join query using FromSqlRaw...
 Loaded 45 images
 Loaded 45 images
 Loaded 45 images
 Loaded 45 images
 Loaded 45 images
SQL Query run 5x in 2663ms (532ms per run).
```

You can see that by eagerly loading the data, the unfiltered inner join loads the entire `ImageTags` table 
(1.5m records) which is extremely slow - taking 1.5s per run. Doing the filtered join results in the query 
running significantly faster.

# How this should be fixed

The fix for this issue is to change the EFCore SQL generation to not use an unfiltered select/join on the
query. So instead of the query:

```
SELECT "b"."ImageId", "i"."ImageId"
FROM "BasketEntries" AS "b"
INNER JOIN "Images" AS "i" ON "b"."ImageId" = "i"."ImageId"
LEFT JOIN (
   SELECT "i0"."ImageId", "i0"."TagId", "t"."TagId" AS "TagId0"
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
INNER JOIN "ImageTags" AS "i0" ON "i"."ImageId" = "i0"."ImageId"
INNER JOIN "Tags" as "t" on "i0".tagId = t.TagID
ORDER BY "b"."ImageId", "i"."ImageId", "i0"."ImageId", "i0"."TagId"
```
Which runs in 1/10 of the time that the EFCore-generated query runs.
