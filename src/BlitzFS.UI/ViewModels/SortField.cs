namespace BlitzFS.UI.ViewModels
{
    /// <summary>
    /// 檔案排序欄位
    /// </summary>
    public enum SortField
    {
        /// <summary>
        /// 檔案/資料夾名稱
        /// </summary>
        Name,

        /// <summary>
        /// 檔案大小
        /// </summary>
        Size,

        /// <summary>
        /// 修改日期與時間
        /// </summary>
        ModifiedDate,

        /// <summary>
        /// 檔案類型 (副檔名)
        /// </summary>
        Type
    }

    /// <summary>
    /// 排序方向
    /// </summary>
    public enum SortDirection
    {
        /// <summary>
        /// 遞增 (A-Z / 舊到新 / 小到大)
        /// </summary>
        Ascending,

        /// <summary>
        /// 遞減 (Z-A / 新到舊 / 大到小)
        /// </summary>
        Descending
    }
}
