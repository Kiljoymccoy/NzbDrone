﻿using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Composition;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers
{
    public interface IIndexerFactory : IProviderFactory<IIndexer, IndexerDefinition>
    {

    }

    public class IndexerFactory : ProviderFactory<IIndexer, IndexerDefinition>, IIndexerFactory
    {
        private readonly IIndexerRepository _providerRepository;
        private readonly INewznabTestService _newznabTestService;

        public IndexerFactory(IIndexerRepository providerRepository,
                              IEnumerable<IIndexer> providers,
                              IContainer container, 
                              IEventAggregator eventAggregator, 
                              INewznabTestService newznabTestService, 
                              Logger logger)
            : base(providerRepository, providers, container, eventAggregator, logger)
        {
            _providerRepository = providerRepository;
            _newznabTestService = newznabTestService;
        }

        protected override void InitializeProviders()
        {

        }

        protected override List<IndexerDefinition> Active()
        {
            return base.Active().Where(c => c.Enable).ToList();
        }

        public override IndexerDefinition Create(IndexerDefinition definition)
        {
            if (definition.Implementation == typeof(Newznab.Newznab).Name)
            {
                var indexer = GetInstance(definition);
                _newznabTestService.Test(indexer);
            }

            return base.Create(definition);
        }

        protected override IndexerDefinition GetTemplate(IIndexer provider)
        {
            var definition = base.GetTemplate(provider);

            definition.Protocol = provider.Protocol;

            return definition;
        }
    }
}