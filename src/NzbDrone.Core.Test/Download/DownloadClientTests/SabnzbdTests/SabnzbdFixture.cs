using System;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.Sabnzbd;
using NzbDrone.Core.Download.Clients.Sabnzbd.Responses;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using System.Collections.Generic;

namespace NzbDrone.Core.Test.Download.DownloadClientTests.SabnzbdTests
{
    [TestFixture]
    public class SabnzbdFixture : DownloadClientFixtureBase<Sabnzbd>
    {
        private SabnzbdQueue _queued;
        private SabnzbdHistory _failed;
        private SabnzbdHistory _completed;

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new DownloadClientDefinition();
            Subject.Definition.Settings = new SabnzbdSettings
                                          {
                                              Host = "192.168.5.55",
                                              Port = 2222,
                                              ApiKey = "5c770e3197e4fe763423ee7c392c25d1",
                                              Username = "admin",
                                              Password = "pass",
                                              TvCategory = "tv",
                                              RecentTvPriority = (int)SabnzbdPriority.High
                                          };
            _queued = new SabnzbdQueue
                {
                    Paused = false,
                    Items = new List<SabnzbdQueueItem>()
                    {
                        new SabnzbdQueueItem
                        {
                            Status = SabnzbdDownloadStatus.Downloading,
                            Size = 1000,
                            Sizeleft = 10,
                            Timeleft = TimeSpan.FromSeconds(10),
                            Category = "tv",
                            Id = "sabnzbd_nzb12345",
                            Title = "Droned.S01E01.Pilot.1080p.WEB-DL-DRONE"
                        }
                    }
                };

            _failed = new SabnzbdHistory
                {
                    Items = new List<SabnzbdHistoryItem>()
                    {
                        new SabnzbdHistoryItem
                        {
                            Status = SabnzbdDownloadStatus.Failed,
                            Size = 1000,
                            Category = "tv", 
                            Id = "sabnzbd_nzb12345",
                            Title = "Droned.S01E01.Pilot.1080p.WEB-DL-DRONE"
                        }
                    }
                };

            _completed = new SabnzbdHistory
                {
                    Items = new List<SabnzbdHistoryItem>()
                    {
                        new SabnzbdHistoryItem
                        {
                            Status = SabnzbdDownloadStatus.Completed,
                            Size = 1000,
                            Category = "tv", 
                            Id = "sabnzbd_nzb12345",
                            Title = "Droned.S01E01.Pilot.1080p.WEB-DL-DRONE",
                            Storage = "somedirectory"
                        }
                    }
                };
        }

        protected void WithFailedDownload()
        {
            Mocker.GetMock<ISabnzbdProxy>()
                .Setup(s => s.DownloadNzb(It.IsAny<Stream>(), It.IsAny<String>(), It.IsAny<String>(), It.IsAny<int>(), It.IsAny<SabnzbdSettings>()))
                .Returns((SabnzbdAddResponse)null);
        }

        protected void WithSuccessfulDownload()
        {
            Mocker.GetMock<ISabnzbdProxy>()
                .Setup(s => s.DownloadNzb(It.IsAny<Stream>(), It.IsAny<String>(), It.IsAny<String>(), It.IsAny<int>(), It.IsAny<SabnzbdSettings>()))
                .Returns(new SabnzbdAddResponse()
                {
                    Status = true,
                    Ids = new List<string> { "sabznbd_nzo12345" }
                });
        }

        protected virtual void WithQueue(SabnzbdQueue queue)
        {
            if (queue == null)
            {
                queue = new SabnzbdQueue() { Items = new List<SabnzbdQueueItem>() };
            }

            Mocker.GetMock<ISabnzbdProxy>()
                .Setup(s => s.GetQueue(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<SabnzbdSettings>()))
                .Returns(queue);
        }

        protected virtual void WithHistory(SabnzbdHistory history)
        {
            if (history == null)
                history = new SabnzbdHistory() { Items = new List<SabnzbdHistoryItem>() };

            Mocker.GetMock<ISabnzbdProxy>()
                .Setup(s => s.GetHistory(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<SabnzbdSettings>()))
                .Returns(history);
        }

        [Test]
        public void GetItems_should_return_no_items_when_queue_is_empty()
        {
            WithQueue(null);
            WithHistory(null);

            Subject.GetItems().Should().BeEmpty();
        }

        [TestCase(SabnzbdDownloadStatus.Grabbing)]
        [TestCase(SabnzbdDownloadStatus.Queued)]
        public void queued_item_should_have_required_properties(SabnzbdDownloadStatus status)
        {
            _queued.Items.First().Status = status;

            WithQueue(_queued);
            WithHistory(null);
            
            var result = Subject.GetItems().Single();

            VerifyQueued(result);
            result.RemainingTime.Should().NotBe(TimeSpan.Zero);
        }

        [TestCase(SabnzbdDownloadStatus.Paused)]
        public void paused_item_should_have_required_properties(SabnzbdDownloadStatus status)
        {
            _queued.Items.First().Status = status;

            WithQueue(_queued);
            WithHistory(null);

            var result = Subject.GetItems().Single();

            VerifyPaused(result);
        }

        [TestCase(SabnzbdDownloadStatus.Checking)]
        [TestCase(SabnzbdDownloadStatus.Downloading)]
        [TestCase(SabnzbdDownloadStatus.QuickCheck)]
        [TestCase(SabnzbdDownloadStatus.Verifying)]
        [TestCase(SabnzbdDownloadStatus.Repairing)]
        [TestCase(SabnzbdDownloadStatus.Fetching)]
        [TestCase(SabnzbdDownloadStatus.Extracting)]
        [TestCase(SabnzbdDownloadStatus.Moving)]
        [TestCase(SabnzbdDownloadStatus.Running)]
        public void downloading_item_should_have_required_properties(SabnzbdDownloadStatus status)
        {
            _queued.Items.First().Status = status;

            WithQueue(_queued);
            WithHistory(null);

            var result = Subject.GetItems().Single();

            VerifyDownloading(result);
            result.RemainingTime.Should().NotBe(TimeSpan.Zero);
        }

        [Test]
        public void completed_download_should_have_required_properties()
        {
            WithQueue(null);
            WithHistory(_completed);

            var result = Subject.GetItems().Single();

            VerifyCompleted(result);
        }

        [Test]
        public void failed_item_should_have_required_properties()
        {
            _completed.Items.First().Status = SabnzbdDownloadStatus.Failed;

            WithQueue(null);
            WithHistory(_completed);

            var result = Subject.GetItems().Single();

            VerifyFailed(result);
        }

        [Test]
        public void Download_should_return_unique_id()
        {
            WithSuccessfulDownload();

            var remoteEpisode = CreateRemoteEpisode();

            var id = Subject.Download(remoteEpisode);

            id.Should().NotBeNullOrEmpty();
        }

        [Test]
        public void GetItems_should_ignore_downloads_from_other_categories()
        {
            _completed.Items.First().Category = "myowncat";

            WithQueue(null);
            WithHistory(_completed);

            var items = Subject.GetItems();

            items.Should().BeEmpty();
        }

        [Test]
        public void Download_should_use_sabRecentTvPriority_when_recentEpisode_is_true()
        {
            Mocker.GetMock<ISabnzbdProxy>()
                    .Setup(s => s.DownloadNzb(It.IsAny<Stream>(), It.IsAny<String>(), It.IsAny<String>(), (int)SabnzbdPriority.High, It.IsAny<SabnzbdSettings>()))
                    .Returns(new SabnzbdAddResponse());

            var remoteEpisode = CreateRemoteEpisode();
            remoteEpisode.Episodes = Builder<Episode>.CreateListOfSize(1)
                                                      .All()
                                                      .With(e => e.AirDate = DateTime.Today.ToString(Episode.AIR_DATE_FORMAT))
                                                      .Build()
                                                      .ToList();

            Subject.Download(remoteEpisode);

            Mocker.GetMock<ISabnzbdProxy>()
                  .Verify(v => v.DownloadNzb(It.IsAny<Stream>(), It.IsAny<String>(), It.IsAny<String>(), (int)SabnzbdPriority.High, It.IsAny<SabnzbdSettings>()), Times.Once());
        }
    }
}
