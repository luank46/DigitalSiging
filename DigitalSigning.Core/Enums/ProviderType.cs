using System;

namespace DigitalSigning.Core.Enums
{
    /// <summary>
    /// Loại nhà cung cấp ký số (CA provider)
    /// </summary>
    public enum ProviderType
    {
        /// <summary>
        /// Cổng giáo dục không chỉ định
        /// </summary>
        None = 0,

        /// <summary>
        /// Cổng giáo dục Việt Nam (VNPT)
        /// </summary>
        Vnpt = 1,

        /// <summary>
        /// Cổng giáo dục Viettel
        /// </summary>
        Viettel = 2,

        /// <summary>
        /// Cổng giáo dục BKAV
        /// </summary>
        Bkav = 3,

        /// <summary>
        /// Cổng giáo dục MISA
        /// </summary>
        Misa = 4,

        /// <summary>
        /// Cổng giáo dục GCC (Smart Card)
        /// </summary>
        Gcc = 5
    }
}