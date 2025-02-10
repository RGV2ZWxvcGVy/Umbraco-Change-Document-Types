using Project.Core.NotificationHandlers;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace Project.Core.Composers
{
    public class CustomComposer : IComposer
    {
        public void Compose(IUmbracoBuilder builder)
        {
            // Handles the logic for changing all of the document types of the nodes under the home node after publish
            builder.AddNotificationHandler<ContentPublishedNotification, ChangeDocTypeContentPublishedNotificationHandler>();
        }
    }
}
