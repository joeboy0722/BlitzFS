using BlitzFS.Bridge;

namespace BlitzFS.UI.ViewModels
{
    /// <summary>
    /// 傳輸進度與狀態監控面板 ViewModel
    /// </summary>
    public class TransferViewModel : ViewModelBase
    {
        private readonly CoreEngineWrapper _engine;
        private bool _isTransferring;
        private bool _isPaused;
        private string _currentFileName = string.Empty;
        private double _progressPercentage;
        private double _speedMBps;
        private uint _processedFiles;
        private uint _totalFiles;

        public bool IsTransferring
        {
            get => _isTransferring;
            set => SetProperty(ref _isTransferring, value);
        }

        public bool IsPaused
        {
            get => _isPaused;
            set => SetProperty(ref _isPaused, value);
        }

        public string CurrentFileName
        {
            get => _currentFileName;
            set => SetProperty(ref _currentFileName, value);
        }

        public double ProgressPercentage
        {
            get => _progressPercentage;
            set => SetProperty(ref _progressPercentage, value);
        }

        public double SpeedMBps
        {
            get => _speedMBps;
            set => SetProperty(ref _speedMBps, value);
        }

        public uint ProcessedFiles
        {
            get => _processedFiles;
            set => SetProperty(ref _processedFiles, value);
        }

        public uint TotalFiles
        {
            get => _totalFiles;
            set => SetProperty(ref _totalFiles, value);
        }

        public string StatusSummary => $"{ProcessedFiles}/{TotalFiles} 檔案 ({SpeedMBps:0.0} MB/s)";

        public TransferViewModel(CoreEngineWrapper engine)
        {
            _engine = engine;
        }

        public void UpdateProgress(in TransferProgressInfo info)
        {
            CurrentFileName = info.CurrentFileName ?? string.Empty;
            ProgressPercentage = info.ProgressPercentage;
            SpeedMBps = info.SpeedMBps;
            ProcessedFiles = info.ProcessedFiles;
            TotalFiles = info.TotalFiles;
            OnPropertyChanged(nameof(StatusSummary));

            if (info.ProcessedFiles >= info.TotalFiles && info.TotalFiles > 0)
            {
                IsTransferring = false;
            }
        }

        public void Pause()
        {
            _engine.PauseTransfer();
            IsPaused = true;
        }

        public void Resume()
        {
            _engine.ResumeTransfer();
            IsPaused = false;
        }

        public void Cancel()
        {
            _engine.CancelTransfer();
            IsTransferring = false;
            IsPaused = false;
        }
    }
}
