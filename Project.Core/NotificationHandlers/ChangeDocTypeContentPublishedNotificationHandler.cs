using Flip.Services;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.PublishedModels;
using Umbraco.Extensions;

namespace Project.Core.NotificationHandlers
{
    public class ChangeDocTypeContentPublishedNotificationHandler : INotificationHandler<ContentPublishedNotification>
    {
        private readonly IContentService _contentService;
        private readonly IContentTypeService _contentTypeService;
        private readonly ICoreScopeProvider _coreScopeProvider;
        private readonly IFlipService _flipService;
        private readonly ILogger<ChangeDocTypeContentPublishedNotificationHandler> _logger;
        private readonly IPublishedContentQuery _publishedContentQuery;

        /// <summary>
        /// The suffix that is added to the new document type structure, to prevent conflicts to the existing structure.
        /// </summary>
        private const string NewDocumentTypeSuffix = "New";

        /// <summary>
        /// The list of nodes that have been published, when the 'PublishNodesOnly' checkbox is enabled.
        /// </summary>
        private readonly List<IContent> _publishedNodes = new();

        /// <summary>
        /// The list of nodes where the document type has been changed.
        /// </summary>
        private readonly List<IContent> _changedNodes = new();

        /// <summary>
        /// The list of nodes where the document type change has failed.
        /// </summary>
        private readonly List<IContent> _failedNodes = new();


        public ChangeDocTypeContentPublishedNotificationHandler(IContentService contentService, IContentTypeService contentTypeService, ICoreScopeProvider coreScopeProvider, IFlipService flipService, ILogger<ChangeDocTypeContentPublishedNotificationHandler> logger, IPublishedContentQuery publishedContentQuery)
        {
            _contentService = contentService;
            _contentTypeService = contentTypeService;
            _coreScopeProvider = coreScopeProvider;
            _flipService = flipService;
            _logger = logger;
            _publishedContentQuery = publishedContentQuery;
        }


        /// <summary>
        /// Handles the logic for changing all of the document types of the nodes under the home node after publish so it can handle multiple nodes instead of one at a time.
        /// Also handles bulk publishing of nodes to update the cache, since the 'Publish with descendants' only works when properties are dirty.
        /// </summary>
        /// <param name="notification">The notification model that contains the entity that has been published.</param>
        public void Handle(ContentPublishedNotification notification)
        {
            foreach (var node in notification.PublishedEntities)
            {
                // Restrict the migration of document types to the 'MigrationSettings' document type
                if (!node.ContentType.Alias.Equals(MigrationSettings.ModelTypeAlias) || _publishedContentQuery.Content(node.Id) is not MigrationSettings migrationSettings)
                {
                    continue;
                }

                // Only execute the migration if the checkbox for flipping (changing) document types is enabled
                if (!migrationSettings.EnableDocumentTypeFlipper)
                {
                    continue;
                }

                // Get the home node based on the current or new document type structure
                var home = _publishedContentQuery.Content(node.Id).AncestorOrSelf<Home>() as IPublishedContent ?? _publishedContentQuery.Content(node.Id).AncestorOrSelf($"{Home.ModelTypeAlias}{NewDocumentTypeSuffix}");
                var allDescendants = _contentService.GetPagedDescendants(home.Id, 0, int.MaxValue, out _).Prepend(_contentService.GetById(home.Id)).OrderBy(x => x.SortOrder);

                // Normally using IPublishedContent would be best practice because of the performance, but we also need unpublished content, so we iterate through IContent.
                foreach (var descendant in allDescendants)
                {
                    // Skip the migration if the provided document type is not applicable for the migration
                    if ((descendant.ContentType.Alias[^3..].Equals(NewDocumentTypeSuffix) && migrationSettings.MigrationType && !migrationSettings.PublishNodesOnly) ||
                        (!descendant.ContentType.Alias[^3..].Equals(NewDocumentTypeSuffix) && !migrationSettings.MigrationType && !migrationSettings.PublishNodesOnly))
                    {
                        continue;
                    }

                    // Publish the nodes if the checkbox for publishing nodes is enabled
                    if (migrationSettings.PublishNodesOnly)
                    {
                        if (descendant.Published)
                        {
                            try
                            {
                                // Create a new scope to avoid further events from being triggered
                                using var scope = _coreScopeProvider.CreateCoreScope(autoComplete: true);
                                using var _ = scope.Notifications.Suppress();
                                _contentService.SaveAndPublish(descendant);
                                _publishedNodes.Add(descendant);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError("[Flip.Umbraco] - Could not publish the content node: '{Name}' - Umbraco id: '{Id}'.\n{Exception}", descendant.Name, descendant.Id, ex);
                                _failedNodes.Add(descendant);
                            }
                        }
                    }
                    else
                    {
                        try
                        {
                            TryChangeDocumentType(descendant, migrationSettings.MigrationType);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("[Flip.Umbraco] - Could not change the document type of node: '{NodeName}' - Umbraco id: '{Id}'.\n{Exception}", descendant.Name, descendant.Id, ex);

                            if (!_failedNodes.Contains(descendant))
                            {
                                _failedNodes.Add(descendant);
                            }
                        }
                    }
                }

                // Log the results of the migration
                if (migrationSettings.EnableDocumentTypeFlipper && migrationSettings.PublishNodesOnly)
                {
                    _logger.LogInformation("[Flip.Umbraco] - Successfully published {Count} nodes.", _publishedNodes.Count);

                    if (_failedNodes.Any())
                    {
                        _logger.LogWarning("[Flip.Umbraco] - Failed to publish {Count} nodes.", _failedNodes.Count);
                    }
                }

                if (migrationSettings.EnableDocumentTypeFlipper && !migrationSettings.PublishNodesOnly)
                {
                    _logger.LogInformation("[Flip.Umbraco] - Successfully changed the document type of {Count} nodes.", _changedNodes.Count);

                    if (_failedNodes.Any())
                    {
                        _logger.LogWarning("[Flip.Umbraco] - Failed to change the document type of {Count} nodes.", _failedNodes.Count);
                    }
                }
            }
        }

        /// <summary>
        /// Changes the document type of an existing content node.
        /// </summary>
        /// <param name="node">The content node that is being altered.</param>
        /// <param name="migrationType">The type of migration to perform. True = Migration to new document type structure. False = Rollback.</param>
        private void TryChangeDocumentType(IContent node, bool migrationType)
        {
            // Get the new document type based on the migration type, and alias of the source (current) document type, and add 'New' for the target type
            var targetContentType = migrationType ? _contentTypeService.Get($"{node.ContentType.Alias}New") : _contentTypeService.Get($"{node.ContentType.Alias[..^3]}");
            if (targetContentType == null)
            {
                _logger.LogWarning("[Flip.Umbraco] - Could not find a target document type for the alias: '{Alias}'", node.ContentType.Alias);
                return;
            }

            // Prepare the model for changing the document type
            var model = _flipService.GetContentModel(node.Id);
            // Set the target document type and template
            model.ContentTypeId = targetContentType.Id;
            model.ContentTypeName = targetContentType.Name;
            model.TemplateId = targetContentType.DefaultTemplateId;

            // Map the new alias of the properties to the old ones (if they exist), because the alias names are exactly the same.
            var properties = model.Properties.ToList();
            foreach (var prop in properties.Where(p => targetContentType.PropertyTypeExists(p.Alias)))
            {
                prop.NewAlias = prop.Alias;
            }
            model.Properties = properties;

            if (_flipService.TryChangeContentType(model, out string message))
            {
                _logger.LogInformation("[Flip.Umbraco] - Successfully changed the document type of node: '{NodeName}' to type: '{ContentType}'.\n{Message}", node.Name, targetContentType.Name, message);
                _changedNodes.Add(node);
            }
            else
            {
                _logger.LogWarning("[Flip.Umbraco] - Could not change the document type of node: '{NodeName}' to type: '{ContentType}'.\n{Message}", node.Name, targetContentType.Name, message);
                _failedNodes.Add(node);
            }
        }
    }
}
