# Entity Coding for Rock RMS

The purpose of this project is to provide Rock RMS with the ability to export and
import entity data between systems. For example, if you have built a Workflow and
wish to share it with the community you can export the Workflow and then others
can use this same code to import that Workflow into their system with minimal
changes required.

## Encoding (export)

Encoding is handled by the `EntityCoder` class and exporter helper classes. The
main EntityCoder class works by taking a single "root" entity to be encoded as
well as a exporter. The exporter is responsible for deciding what property paths
should be followed. For example, when exporting a Page tree you do not want to follow
the root parent page since that would end up exporting the entire page tree, not just
the section you wanted.

The EntityCoder class will then follow all referenced and child entities. Referenced
entities are those that have a 1:1 relationship, such as the property `CategoryId`.
That property references a specific entity (a Category) that must exist before this
entity (Defined Type) can be created. A child entity comes from a 1:many relationship,
such as the virtual enumerable property `DefinedValues`. This identifies entities that
are children of this entity and should be created after this entity.

There are two Entity Types that are never exported because they must inherintly exist
on the target system already, they are instead referenced only:

1. Entity Type
2. Field Type

In addition to these special entity types, there are other entities that require special
handling. As such, there are a number of `IEntityProcessor` classes defined that allow
for non-traditional processing. One example is the `WorkflowActionFormProcessor`. A
WorkflowActionForm encodes some data as a string, which includes Guid references to
Defined Values. Since these cannot tracked by normal means, the Entity Processor examines
the data of the entity and adds any extra referenced or child items to the queue as well
as handles restoring those values (since the value may need to be updated to match the
new target entity).

Once all the entities have been enqueued and pre-processed then the `GetExportedEntities`
method is called to do the final encoding and return an object that can then be JSON
encoded for transmission.

## Decoding (import)

Decoding is handled by the `EntityDecoder` class. There is currently no helper class
needed. Importing data into the system is handled very straightforward, as all the heavy
lifting is done at export time. The list of entities to be imported is pre-ordered to be
the correct order of operations so each entity is created from the data available and
saved to the database. Any references are resolved before saving. An entity can be flagged
for getting a new Guid or using the existing Guid (for example, Attributes do not need a
new Guid value, but Attribute Values do).

The `IEntityProcessor` classes, as mentioned above, also provide import post-processing
to make sure the entity data is correct for the new system. There is also special
handling for Attributes (and by proxy, AttributeValues). Since an attribute may already
exist for that entity type but have a different Guid, special checking is done on the Key
and qualifier columns.

The entire import process is done within a single transaction, ensuring that either every
entity is created successfully, or the entire operation is aborted and no changes are made
to the database.

## Exporters

The primary purpose of exporters is to identify which entities need to be encoded and if
they need to have new Guid values generated on import. For example, a Defined Type wouldn't
necessarily need a new Guid value generated because if you imported more things you would
likely want them to go into the same Defined Type. This will, however, not always be the
case so it is important to consider final usage when deciding if an entity will get a new
Guid.

An exporter also decides if a path to an entity should be considered critical. This is not
really used in practice, but is provided for later ability to let the exporting user decide
to not export some child items. Any reference that is required for the export to function
correctly should be marked as critical. For example, when exporting a page structure, the
Pages might be considered critical while the blocks be considered optional. This would allow
the user to export the page _structure_ but not the content on the pages.

Finally, the exporter is responsible for identifying user references. These are values that
will be provided during import later. As mentioned earlier, in the case of exporting a
Workflow, we don't want to export the entire category tree. It's better to let the user pick
which category to import the Workflow into. Usually these references will be on the root
entity only, but down the road there was desire to allow the user to see a preview of what
would be exported and provide user values for any of those items (example, a page export
with a link to a Detail page and the intention is to have the user select what detail page
to link to).

## Entity Paths

During the encoding process, all entities are queued up first and the path to reach each
entity is tracked as well. We track this since a single entity may be reached by multiple
paths and knowing which path reached an entity may be important. An example of this is the
Defined Type for HTML Buttons in a workflow. Each workflow User Entry Form references one
or more Defined Values which in turn all reference the same Defined Type that they belong
to. While we may reach that Defined Type entity multiple times, it is only encoded once but
each path we used to reach it is tracked.

## Entity References

If one entity references another, such as a Defined Value referencing it's parent Defined Type
then a Reference object is created to track and later restore that reference. The following
reference types are defined, along with their purpose:

1. Guid: A reference to a generic Entity. The entity type and Guid to be looked up are used
to search for a record matching the Guid and then translated into the Id number for storing
on the newly created entity.
2. Entity Type: A reference to an Entity Type. The full class name of the Entity Type is
used to find the Id number.
3. Field Type: A reference to a Field Type. The full class name of the Field Type is used to
find the Id number.
4. User Defined: The user will be asked at import time for the entity to be used in this
reference. Each User Defined reference has a key that is used to get the value during import.
For example, when importing a Workflow we need to know the CategoryId to place it under so
we ask the user.
