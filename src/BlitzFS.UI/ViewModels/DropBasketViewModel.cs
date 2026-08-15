using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using BlitzFS.Bridge;

namespace BlitzFS.UI.ViewModels
{
    /// <summary>
    /// 檔案暫存收集籃 (Drop Basket) ViewModel
    /// 解決使用者跨多個目錄收集零散檔案再一次性批次處理之痛點
    /// </summary>
    public class DropBasketViewModel : ViewModelBase
    {
        public ObservableCollection<FileItemViewModel> CollectedItems { get; } = new();

        public bool HasItems => CollectedItems.Count > 0;
        public string ItemCountText => $"暫存籃 ({CollectedItems.Count} 個項目)";

        public void AddItem(FileItemViewModel? item)
        {
            if (item == null) return;

            // 避免重複加入
            foreach (var existing in CollectedItems)
            {
                if (existing.FullPath == item.FullPath) return;
            }

            CollectedItems.Add(item);
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(ItemCountText));
        }

        public void RemoveItem(FileItemViewModel? item)
        {
            if (item == null) return;
            CollectedItems.Remove(item);
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(ItemCountText));
        }

        public void Clear()
        {
            CollectedItems.Clear();
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(ItemCountText));
        }

        /// <summary>
        /// 將暫存籃內的所有檔案一次性搬移至目標目錄
        /// </summary>
        public async Task MoveAllToAsync(string destinationDirectory, CoreEngineWrapper engine)
        {
            if (string.IsNullOrEmpty(destinationDirectory) || !Directory.Exists(destinationDirectory)) return;

            foreach (var item in CollectedItems)
            {
                string targetPath = Path.Combine(destinationDirectory, item.Name);
                await engine.StartTransferAsync(item.FullPath, targetPath, isMove: true);
            }

            Clear();
        }

        /// <summary>
        /// 將暫存籃內的所有檔案一次性複製至目標目錄
        /// </summary>
        public async Task CopyAllToAsync(string destinationDirectory, CoreEngineWrapper engine)
        {
            if (string.IsNullOrEmpty(destinationDirectory) || !Directory.Exists(destinationDirectory)) return;

            foreach (var item in CollectedItems)
            {
                string targetPath = Path.Combine(destinationDirectory, item.Name);
                await engine.StartTransferAsync(item.FullPath, targetPath, isMove: false);
            }
        }
    }
}
