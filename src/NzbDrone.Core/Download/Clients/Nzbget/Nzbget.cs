﻿using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using Omu.ValueInjecter;

namespace NzbDrone.Core.Download.Clients.Nzbget
{
    public class Nzbget : DownloadClientBase<NzbgetSettings>, IExecute<TestNzbgetCommand>
    {
        private readonly INzbgetProxy _proxy;
        private readonly IHttpProvider _httpProvider;

        public Nzbget(INzbgetProxy proxy,
                      IParsingService parsingService,
                      IHttpProvider httpProvider,
                      Logger logger)
            : base(parsingService, logger)
        {
            _proxy = proxy;
            _httpProvider = httpProvider;
        }

        public override DownloadProtocol Protocol
        {
            get
            {
                return DownloadProtocol.Usenet;
            }
        }

        public override string Download(RemoteEpisode remoteEpisode)
        {
            var url = remoteEpisode.Release.DownloadUrl;
            var title = remoteEpisode.Release.Title + ".nzb";

            string category = Settings.TvCategory;
            int priority = remoteEpisode.IsRecentEpisode() ? Settings.RecentTvPriority : Settings.OlderTvPriority;

            _logger.Info("Adding report [{0}] to the queue.", title);

            using (var nzb = _httpProvider.DownloadStream(url))
            {
                _logger.Info("Adding report [{0}] to the queue.", title);
                var response = _proxy.DownloadNzb(nzb, title, category, priority, Settings);

                return response;
            }
        }

        private IEnumerable<DownloadClientItem> GetQueue()
        {
            List<NzbgetQueueItem> queue;

            try
            {
                queue = _proxy.GetQueue(Settings);
            }
            catch (DownloadClientException ex)
            {
                _logger.ErrorException(ex.Message, ex);
                return Enumerable.Empty<DownloadClientItem>();
            }

            var queueItems = new List<DownloadClientItem>();

            foreach (var item in queue)
            {
                var droneParameter = item.Parameters.SingleOrDefault(p => p.Name == "drone");

                var queueItem = new DownloadClientItem();
                queueItem.DownloadClientId = droneParameter == null ? item.NzbId.ToString() : droneParameter.Value.ToString();
                queueItem.Title = item.NzbName;
                queueItem.TotalSize = MakeInt64(item.FileSizeHi, item.FileSizeLo);
                queueItem.RemainingSize = MakeInt64(item.RemainingSizeHi, item.RemainingSizeLo);
                queueItem.Category = item.Category;

                if (queueItem.TotalSize == MakeInt64(item.PausedSizeHi, item.PausedSizeLo))
                {
                    queueItem.Status = DownloadItemStatus.Paused;
                }
                else if (item.ActiveDownloads == 0 && queueItem.RemainingSize != 0)
                {
                    queueItem.Status = DownloadItemStatus.Queued;
                }
                else
                {
                    queueItem.Status = DownloadItemStatus.Downloading;
                }

                queueItems.Add(queueItem);
            }

            return queueItems;
        }

        private IEnumerable<DownloadClientItem> GetHistory()
        {
            List<NzbgetHistoryItem> history;

            try
            {
                history = _proxy.GetHistory(Settings);
            }
            catch (DownloadClientException ex)
            {
                _logger.ErrorException(ex.Message, ex);
                return Enumerable.Empty<DownloadClientItem>();
            }

            var historyItems = new List<DownloadClientItem>();
            var successStatus = new[] {"SUCCESS", "NONE"};

            foreach (var item in history)
            {
                var droneParameter = item.Parameters.SingleOrDefault(p => p.Name == "drone");

                var historyItem = new DownloadClientItem();
                historyItem.DownloadClient = Definition.Name;
                historyItem.DownloadClientId = droneParameter == null ? item.Id.ToString() : droneParameter.Value.ToString();
                historyItem.Title = item.Name;
                historyItem.TotalSize = MakeInt64(item.FileSizeHi, item.FileSizeLo);
                historyItem.OutputPath = item.DestDir;
                historyItem.Category = item.Category;
                historyItem.Message = String.Format("PAR Status: {0} - Unpack Status: {1} - Move Status: {2} - Script Status: {3} - Delete Status: {4} - Mark Status: {5}", item.ParStatus, item.UnpackStatus, item.MoveStatus, item.ScriptStatus, item.DeleteStatus, item.MarkStatus);
                historyItem.Status = DownloadItemStatus.Completed;

                if (!successStatus.Contains(item.ParStatus) ||
                         !successStatus.Contains(item.UnpackStatus) ||
                         !successStatus.Contains(item.MoveStatus) ||
                         !successStatus.Contains(item.ScriptStatus) ||
                         !successStatus.Contains(item.DeleteStatus) ||
                         !successStatus.Contains(item.MarkStatus))
                {
                    historyItem.Status = DownloadItemStatus.Failed;
                }
                else if (item.MoveStatus != "SUCCESS")
                {
                    historyItem.Status = DownloadItemStatus.Queued;
                }

                historyItems.Add(historyItem);
            }

            return historyItems;
        }

        public override IEnumerable<DownloadClientItem> GetItems()
        {
            foreach (var downloadClientItem in GetQueue().Concat(GetHistory()))
            {
                if (downloadClientItem.Category != Settings.TvCategory) continue;

                downloadClientItem.RemoteEpisode = GetRemoteEpisode(downloadClientItem.Title);
                if (downloadClientItem.RemoteEpisode == null) continue;

                yield return downloadClientItem;
            }
        }

        public override void RemoveItem(string id)
        {
            _proxy.RemoveFromHistory(id, Settings);
        }

        public override void RetryDownload(string id)
        {
            _proxy.RetryDownload(id, Settings);
        }

        public override void Test()
        {
            _proxy.GetVersion(Settings);
        }

        private VersionResponse GetVersion(string host = null, int port = 0, string username = null, string password = null)
        {
            return _proxy.GetVersion(Settings);
        }

        public void Execute(TestNzbgetCommand message)
        {
            var settings = new NzbgetSettings();
            settings.InjectFrom(message);

            _proxy.GetVersion(settings);
        }

        // Javascript doesn't support 64 bit integers natively so json officially doesn't either. 
        // NzbGet api thus sends it in two 32 bit chunks. Here we join the two chunks back together.
        // Simplified decimal example: "42" splits into "4" and "2". To join them I shift (<<) the "4" 1 digit to the left = "40". combine it with "2". which becomes "42" again.
        private Int64 MakeInt64(UInt32 high, UInt32 low)
        {
            Int64 result = high;

            result = (result << 32) | (Int64)low;

            return result;
        }
    }
}