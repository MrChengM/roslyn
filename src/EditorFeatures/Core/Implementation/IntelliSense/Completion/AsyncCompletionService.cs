﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.CodeAnalysis.Editor.Shared.Options;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Experiments;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Shared.TestHooks;
using Microsoft.CodeAnalysis.Shared.Utilities;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.BraceCompletion;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.CodeAnalysis.Editor.Implementation.IntelliSense.Completion
{
    [Export(typeof(IAsyncCompletionService))]
    internal class AsyncCompletionService : ForegroundThreadAffinitizedObject, IAsyncCompletionService
    {
        private readonly IEditorOperationsFactoryService _editorOperationsFactoryService;
        private readonly ITextUndoHistoryRegistry _undoHistoryRegistry;
        private readonly IInlineRenameService _inlineRenameService;
        private readonly IIntelliSensePresenter<ICompletionPresenterSession, ICompletionSession> _completionPresenter;
        private readonly IAsynchronousOperationListener _listener;
        private readonly IEnumerable<Lazy<IBraceCompletionSessionProvider, BraceCompletionMetadata>> _autoBraceCompletionChars;
        private readonly Dictionary<IContentType, ImmutableHashSet<char>> _autoBraceCompletionCharSet;

        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public AsyncCompletionService(
            IThreadingContext threadingContext,
            IEditorOperationsFactoryService editorOperationsFactoryService,
            ITextUndoHistoryRegistry undoHistoryRegistry,
            IInlineRenameService inlineRenameService,
            IAsynchronousOperationListenerProvider listenerProvider,
            [ImportMany] IEnumerable<Lazy<IIntelliSensePresenter<ICompletionPresenterSession, ICompletionSession>, OrderableMetadata>> completionPresenters,
            [ImportMany] IEnumerable<Lazy<IBraceCompletionSessionProvider, BraceCompletionMetadata>> autoBraceCompletionChars)
            : base(threadingContext)
        {
            _editorOperationsFactoryService = editorOperationsFactoryService;
            _undoHistoryRegistry = undoHistoryRegistry;
            _inlineRenameService = inlineRenameService;
            _completionPresenter = ExtensionOrderer.Order(completionPresenters).Select(lazy => lazy.Value).FirstOrDefault();
            _listener = listenerProvider.GetListener(FeatureAttribute.CompletionSet);

            _autoBraceCompletionChars = autoBraceCompletionChars;
            _autoBraceCompletionCharSet = new Dictionary<IContentType, ImmutableHashSet<char>>();
        }

        public bool TryGetController(ITextView textView, ITextBuffer subjectBuffer, out Controller controller)
        {
            AssertIsForeground();

            if (!UseLegacyCompletion(textView, subjectBuffer))
            {
                controller = null;
                return false;
            }

            var autobraceCompletionCharSet = GetAllAutoBraceCompletionChars(subjectBuffer.ContentType);
            controller = Controller.GetInstance(
                ThreadingContext,
                textView, subjectBuffer,
                _editorOperationsFactoryService, _undoHistoryRegistry, _completionPresenter,
                _listener,
                autobraceCompletionCharSet);

            return true;
        }

        private bool UseLegacyCompletion(ITextView textView, ITextBuffer subjectBuffer)
        {
            return true;
        }

        private ImmutableHashSet<char> GetAllAutoBraceCompletionChars(IContentType bufferContentType)
        {
            if (!_autoBraceCompletionCharSet.TryGetValue(bufferContentType, out var set))
            {
                var builder = ImmutableHashSet.CreateBuilder<char>();
                foreach (var completion in _autoBraceCompletionChars)
                {
                    var metadata = completion.Metadata;
                    foreach (var contentType in metadata.ContentTypes)
                    {
                        if (bufferContentType.IsOfType(contentType))
                        {
                            foreach (var ch in metadata.OpeningBraces)
                            {
                                builder.Add(ch);
                            }

                            break;
                        }
                    }
                }

                set = builder.ToImmutable();
                _autoBraceCompletionCharSet[bufferContentType] = set;
            }

            return set;
        }
    }
}
