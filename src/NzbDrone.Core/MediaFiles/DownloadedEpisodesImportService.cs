﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Download;

namespace NzbDrone.Core.MediaFiles
{
    public interface IDownloadedEpisodesImportService
    {
        List<ImportDecision> ProcessFolder(DirectoryInfo directoryInfo, DownloadClientItem downloadClientItem);
        List<ImportDecision> ProcessFile(FileInfo fileInfo, DownloadClientItem downloadClientItem);
    }

    public class DownloadedEpisodesImportService : IDownloadedEpisodesImportService, IExecute<DownloadedEpisodesScanCommand>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskScanService _diskScanService;
        private readonly ISeriesService _seriesService;
        private readonly IParsingService _parsingService;
        private readonly IConfigService _configService;
        private readonly IMakeImportDecision _importDecisionMaker;
        private readonly IImportApprovedEpisodes _importApprovedEpisodes;
        private readonly ISampleService _sampleService;
        private readonly Logger _logger;

        public DownloadedEpisodesImportService(IDiskProvider diskProvider,
            IDiskScanService diskScanService,
            ISeriesService seriesService,
            IParsingService parsingService,
            IConfigService configService,
            IMakeImportDecision importDecisionMaker,
            IImportApprovedEpisodes importApprovedEpisodes,
            ISampleService sampleService,
            Logger logger)
        {
            _diskProvider = diskProvider;
            _diskScanService = diskScanService;
            _seriesService = seriesService;
            _parsingService = parsingService;
            _configService = configService;
            _importDecisionMaker = importDecisionMaker;
            _importApprovedEpisodes = importApprovedEpisodes;
            _sampleService = sampleService;
            _logger = logger;
        }

        public List<ImportDecision> ProcessFolder(DirectoryInfo directoryInfo, DownloadClientItem downloadClientItem)
        {
            var cleanedUpName = GetCleanedUpFolderName(directoryInfo.Name);
            var series = _parsingService.GetSeries(cleanedUpName);
            var quality = QualityParser.ParseQuality(cleanedUpName);
            _logger.Debug("{0} folder quality: {1}", cleanedUpName, quality);

            if (series == null)
            {
                _logger.Debug("Unknown Series {0}", cleanedUpName);
                return new List<ImportDecision>();
            }

            var videoFiles = _diskScanService.GetVideoFiles(directoryInfo.FullName);

            var decisions = _importDecisionMaker.GetImportDecisions(videoFiles.ToList(), series, true, quality);

            var importedDecisions = _importApprovedEpisodes.Import(decisions, true, downloadClientItem);

            if (!downloadClientItem.IsReadOnly && importedDecisions.Any() && ShouldDeleteFolder(directoryInfo))
            {
                _logger.Debug("Deleting folder after importing valid files");
                _diskProvider.DeleteFolder(directoryInfo.FullName, true);
            }

            return importedDecisions;
        }

        public List<ImportDecision> ProcessFile(FileInfo fileInfo, DownloadClientItem downloadClientItem)
        {
            var series = _parsingService.GetSeries(Path.GetFileNameWithoutExtension(fileInfo.Name));

            if (series == null)
            {
                _logger.Debug("Unknown Series for file: {0}", fileInfo.Name);
                return new List<ImportDecision>();
            }

            var decisions = _importDecisionMaker.GetImportDecisions(new List<string>() { fileInfo.FullName }, series, true, null);

            var importedDecisions = _importApprovedEpisodes.Import(decisions, true, downloadClientItem);

            return importedDecisions;
        }

        private void ProcessDownloadedEpisodesFolder()
        {
            var downloadedEpisodesFolder = _configService.DownloadedEpisodesFolder;

            if (String.IsNullOrEmpty(downloadedEpisodesFolder))
            {
                _logger.Warn("Drone Factory folder is not configured");
                return;
            }

            if (!_diskProvider.FolderExists(downloadedEpisodesFolder))
            {
                _logger.Warn("Drone Factory folder [{0}] doesn't exist.", downloadedEpisodesFolder);
                return;
            }

            foreach (var subFolder in _diskProvider.GetDirectories(downloadedEpisodesFolder))
            {
                ProcessFolder(subFolder);
            }

            foreach (var videoFile in _diskScanService.GetVideoFiles(downloadedEpisodesFolder, false))
            {
                try
                {
                    ProcessVideoFile(videoFile);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("An error has occurred while importing video file" + videoFile, ex);
                }
            }
        }

        private List<ImportDecision> ProcessFolder(DirectoryInfo directoryInfo)
        {
            var cleanedUpName = GetCleanedUpFolderName(directoryInfo.Name);
            var series = _parsingService.GetSeries(cleanedUpName);
            var quality = QualityParser.ParseQuality(cleanedUpName);
            _logger.Debug("{0} folder quality: {1}", cleanedUpName, quality);

            if (series == null)
            {
                _logger.Debug("Unknown Series {0}", cleanedUpName);
                return new List<ImportDecision>();
            }

            var videoFiles = _diskScanService.GetVideoFiles(directoryInfo.FullName);

            foreach (var videoFile in videoFiles)
            {
                if (_diskProvider.IsFileLocked(videoFile))
                {
                    _logger.Debug("[{0}] is currently locked by another process, skipping", videoFile);
                    return new List<ImportDecision>();
                }
            }

            return ProcessFiles(series, quality, videoFiles);
        }

        private void ProcessVideoFile(string videoFile)
        {
            var series = _parsingService.GetSeries(Path.GetFileNameWithoutExtension(videoFile));

            if (series == null)
            {
                _logger.Debug("Unknown Series for file: {0}", videoFile);
                return;
            }

            if (_diskProvider.IsFileLocked(videoFile))
            {
                _logger.Debug("[{0}] is currently locked by another process, skipping", videoFile);
                return;
            }

            ProcessFiles(series, null, videoFile);
        }

        private List<ImportDecision> ProcessFiles(Series series, QualityModel quality, params string[] videoFiles)
        {
            var decisions = _importDecisionMaker.GetImportDecisions(videoFiles.ToList(), series, true, quality);
            return _importApprovedEpisodes.Import(decisions, true);
        }

        private void ProcessFolder(string path)
        {
            Ensure.That(path, () => path).IsValidPath();

            try
            {
                if (_seriesService.SeriesPathExists(path))
                {
                    _logger.Warn("Unable to process folder that contains sorted TV Shows");
                    return;
                }

                var directoryFolderInfo = new DirectoryInfo(path);
                var importedFiles = ProcessFolder(directoryFolderInfo);
                
                if (importedFiles.Any() && ShouldDeleteFolder(directoryFolderInfo))
                {
                    _logger.Debug("Deleting folder after importing valid files");
                    _diskProvider.DeleteFolder(path, true);
                }
            }
            catch (Exception e)
            {
                _logger.ErrorException("An error has occurred while importing folder: " + path, e);
            }
        }

        private string GetCleanedUpFolderName(string folder)
        {
            folder = folder.Replace("_UNPACK_", "")
                           .Replace("_FAILED_", "");

            return folder;
        }

        private bool ShouldDeleteFolder(DirectoryInfo directoryInfo)
        {
            var videoFiles = _diskScanService.GetVideoFiles(directoryInfo.FullName);
            var cleanedUpName = GetCleanedUpFolderName(directoryInfo.Name);
            var series = _parsingService.GetSeries(cleanedUpName);

            foreach (var videoFile in videoFiles)
            {
                var episodeParseResult = Parser.Parser.ParseTitle(Path.GetFileName(videoFile));

                if (episodeParseResult == null)
                {
                    _logger.Warn("Unable to parse file on import: [{0}]", videoFile);
                    return false;
                }

                var size = _diskProvider.GetFileSize(videoFile);
                var quality = QualityParser.ParseQuality(videoFile);

                if (!_sampleService.IsSample(series, quality, videoFile, size,
                    episodeParseResult.SeasonNumber))
                {
                    _logger.Warn("Non-sample file detected: [{0}]", videoFile);
                    return false;
                }
            }

            return true;
        }

        public void Execute(DownloadedEpisodesScanCommand message)
        {
            if (message.Path.IsNullOrWhiteSpace())
            {
                ProcessDownloadedEpisodesFolder();
            }

            else
            {
                ProcessFolder(message.Path);
            }
        }
    }
}