using System.Collections.Generic;
using System.Linq;
using DigitalSigning.Core.Settings;

namespace DigitalSigning.Core.Helpers
{
    /// <summary>
    /// Helper lấy URL upload/view file theo năm học.
    /// Mapping từ Core.Helper.FileConfigHelper cũ.
    /// </summary>
    public static class FileConfigHelper
    {
        public static string GetUrlUploadFile(IReadOnlyList<FileConfig> listFileConfig, int maNamHoc)
        {
            if (listFileConfig != null && listFileConfig.Count > 0)
            {
                var configFile = listFileConfig.FirstOrDefault(x => x.MaNamHoc == maNamHoc);
                if (configFile != null)
                    return configFile.UrlUploadFile ?? string.Empty;

                // Fallback to newest year
                return listFileConfig.OrderByDescending(i => i.MaNamHoc).First().UrlUploadFile ?? string.Empty;
            }
            return string.Empty;
        }

        public static string GetUrlViewFile(IReadOnlyList<FileConfig> listFileConfig, int maNamHoc)
        {
            if (listFileConfig != null && listFileConfig.Count > 0)
            {
                var configFile = listFileConfig.FirstOrDefault(x => x.MaNamHoc == maNamHoc);
                if (configFile != null)
                    return configFile.UrlViewFile ?? string.Empty;

                // Fallback to newest year
                return listFileConfig.OrderByDescending(i => i.MaNamHoc).First().UrlViewFile ?? string.Empty;
            }
            return string.Empty;
        }
    }
}
