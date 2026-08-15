namespace BlitzFS.UI.ViewModels
{
    /// <summary>
    /// 檔案窗格檢視模式
    /// </summary>
    public enum ViewMode
    {
        /// <summary>
        /// 詳細資訊清單 (極速表格)
        /// </summary>
        Details = 0,

        /// <summary>
        /// 中縮圖網格 (80x80)
        /// </summary>
        MediumIcons = 1,

        /// <summary>
        /// 大縮圖相簿模式 (160x160)
        /// </summary>
        LargeIcons = 2
    }
}
