﻿using System;
using System.Linq;
using JetBrains.Annotations;
using Orchard.Comments.Models;
using Orchard.Comments.Services;
using Orchard.Comments.Settings;
using Orchard.ContentManagement;
using Orchard.ContentManagement.Drivers;
using System.Collections.Generic;
using Orchard.Services;

namespace Orchard.Comments.Drivers {
    [UsedImplicitly]
    public class CommentsPartDriver : ContentPartDriver<CommentsPart> {
        private readonly ICommentService _commentService;
        private readonly IContentManager _contentManager;
        private readonly IEnumerable<IHtmlFilter> _htmlFilters;

        public CommentsPartDriver(
            ICommentService commentService,
            IContentManager contentManager,
            IEnumerable<IHtmlFilter> htmlFilters) {
            _commentService = commentService;
            _contentManager = contentManager;
            _htmlFilters = htmlFilters;
        }

        protected override DriverResult Display(CommentsPart part, string displayType, dynamic shapeHelper) {
            if (part.CommentsShown == false)
                return null;

            var commentsForCommentedContent = _commentService.GetCommentsForCommentedContent(part.ContentItem.Id);
            var pendingCount = new Lazy<int>(() => commentsForCommentedContent.Where(x => x.Status == CommentStatus.Pending).Count());
            var approvedCount = new Lazy<int>(() => commentsForCommentedContent.Where(x => x.Status == CommentStatus.Approved).Count());

            return Combined(
                ContentShape("Parts_ListOfComments",
                    () => {
                        var settings = part.TypePartDefinition.Settings.GetModel<CommentsPartSettings>();

                        // create a hierarchy of shapes
                        var firstLevelShapes = new List<dynamic>();
                        var allShapes = new Dictionary<int, dynamic>();
                        var comments = commentsForCommentedContent.OrderBy(x => x.Position).List().ToList();
                        
                        foreach (var item in comments) {
                            var formatted = _htmlFilters.Where(x => x.GetType().Name.Equals(settings.HtmlFilter, StringComparison.OrdinalIgnoreCase)).Aggregate(item.CommentText, (text, filter) => filter.ProcessContent(text));
                            var shape = shapeHelper.Parts_Comment(FormattedText: formatted, ContentPart: item, ContentItem: item.ContentItem);

                            allShapes.Add(item.Id, shape);
                        }

                        foreach (var item in comments) {
                            var shape = allShapes[item.Id];
                            if (item.RepliedOn.HasValue) {
                                allShapes[item.RepliedOn.Value].Add(shape);
                            }
                            else {
                                firstLevelShapes.Add(shape);
                            }
                        }

                        var list = shapeHelper.List(Items: firstLevelShapes);

                        return shapeHelper.Parts_ListOfComments(List: list, CommentCount: approvedCount.Value);
                    }),
                ContentShape("Parts_CommentForm",
                    () => {

                        var newComment = _contentManager.New("Comment");
                        if (newComment.Has<CommentPart>()) newComment.As<CommentPart>().CommentedOn = part.Id;
                        var editorShape = _contentManager.BuildEditor(newComment);

                        return shapeHelper.Parts_CommentForm(EditorShape: editorShape);
                    }),
                ContentShape("Parts_Comments_Count",
                    () => shapeHelper.Parts_Comments_Count(CommentCount: approvedCount.Value, PendingCount: pendingCount.Value)),
                ContentShape("Parts_Comments_Count_SummaryAdmin",
                    () => shapeHelper.Parts_Comments_Count_SummaryAdmin(CommentCount: approvedCount.Value, PendingCount: pendingCount.Value))
            );
        }

        protected override DriverResult Editor(CommentsPart part, dynamic shapeHelper) {
            return ContentShape("Parts_Comments_Enable",
                                () => {
                                    // if the part is new, then apply threaded comments defaults
                                    if(!part.ContentItem.HasDraft() && !part.ContentItem.HasPublished()) {
                                        var settings = part.TypePartDefinition.Settings.GetModel<CommentsPartSettings>();
                                        part.ThreadedComments = settings.DefaultThreadedComments;
                                    }
                                    return shapeHelper.EditorTemplate(TemplateName: "Parts.Comments.Comments", Model: part, Prefix: Prefix);
                                });
        }

        protected override DriverResult Editor(CommentsPart part, IUpdateModel updater, dynamic shapeHelper) {
            updater.TryUpdateModel(part, Prefix, null, null);
            return Editor(part, shapeHelper);
        }

        protected override void Importing(CommentsPart part, ContentManagement.Handlers.ImportContentContext context) {
            var commentsShown = context.Attribute(part.PartDefinition.Name, "CommentsShown");
            if (commentsShown != null) {
                part.CommentsShown = Convert.ToBoolean(commentsShown);
            }

            var commentsActive = context.Attribute(part.PartDefinition.Name, "CommentsActive");
            if (commentsActive != null) {
                part.CommentsActive = Convert.ToBoolean(commentsActive);
            }

            var threadedComments = context.Attribute(part.PartDefinition.Name, "ThreadedComments");
            if (threadedComments != null) {
                part.ThreadedComments = Convert.ToBoolean(threadedComments);
            }
        }

        protected override void Exporting(CommentsPart part, ContentManagement.Handlers.ExportContentContext context) {
            context.Element(part.PartDefinition.Name).SetAttributeValue("CommentsShown", part.CommentsShown);
            context.Element(part.PartDefinition.Name).SetAttributeValue("CommentsActive", part.CommentsActive);
            context.Element(part.PartDefinition.Name).SetAttributeValue("ThreadedComments", part.ThreadedComments);
        }
    }
}