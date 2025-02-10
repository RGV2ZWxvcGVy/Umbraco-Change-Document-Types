This repository contains some code snippets to change an old document type structure to a new and updated one.
An use case could be that the current structure of document types is out-of-date, uses too much inheritance instead of relying on compositions etc.
Beware that this code is only tested in Umbraco 10.x, so adjustments could be needed if you are using a different version of Umbraco.

**CAUTION**: Just as the Flip.Umbraco package that is within this repository, this is experimental code and should be used with caution.
Make a backup of your database before running the migration.

If you are using Umbraco Cloud, you can add the provided uda files to your project, and update the Umbraco schema.
If not, you can just manually create the document, and data types that are needed based on the provided screenshots.

The code inside the notification handler should execute if you created a new "MigrationSettings" node under the root node of the nodes that you would like to migrate.
The migration should start if you enabled the check box to enable the document type flipper, the check box to migrate to the new structure (off = rollback, on = migrate to new doc types), and disabled the check box to only publish the nodes.
Keep in mind that the aliases of the current document types should exactly match the aliases of the new document types, to correctly complete the migration. Otherwise you could end up missing important content.
This migration is only for swapping (flipping) document types to new desired types.
There is an option to rollback to your previous document types, and an option to only publish the nodes (recommended to use after the migration).

After running the migration you should use the option to only publish the migrated nodes to update the cache.
It is also recommended to reload the memory cache, and rebuild the database cache within the Settings section of Umbraco.

When the migration completed successfully you should be able to see your content nodes having the new document types with the chosen suffix.
After you migrated all of the content nodes, you can delete your old document types, and rename the new document types to the old names, if desired.
If not you should change any custom code that is referring to document type aliases to reflect your new document type aliases.
When using Umbraco Cloud remember to follow the documentation that is available, to correctly push your document type changes to the cloud.
