namespace DigitalSigning.Core.Providers
{
    /// <summary>
    /// Hằng số dùng chung cho tất cả CA providers.
    /// Mapping từ legacy SignHelper.
    /// </summary>
    public static class SignHelper
    {
        // ── Provider names ────────────────────────────────────────────
        public const string NHA_PHAT_HANH_VNPT    = "VNPT";
        public const string NHA_PHAT_HANH_VIETTEL = "VIETTEL";
        public const string NHA_PHAT_HANH_BKAV    = "BKAVCA";
        public const string NHA_PHAT_HANH_HSM     = "BAN_CO_YEU";
        public const string NHA_PHAT_HANH_MISA    = "MISA-CA";

        // ── Signing statuses ──────────────────────────────────────────
        public const string MA_TRANG_THAI_DA_KY                 = "DA_KY";
        public const string MA_TRANG_THAI_TU_CHOI_KY            = "TU_CHOI_KY";
        public const string MA_TRANG_THAI_KY_KHONG_THANH_CONG   = "KY_KHONG_THANH_CONG";
        public const string MA_TRANG_THAI_HET_HAN_KY            = "HET_HAN";
        public const string MA_TRANG_THAI_CHUNG_THU_SO_HET_HAN  = "CHUNG_THU_SO_HET_HAN";

        // ── Encryption algorithms ────────────────────────────────────
        public const string ENCRYPT_ALGORITHM_ECDSA = "ECDSA";
        public const string ENCRYPT_ALGORITHM_RSA   = "RSA";

        // ── Certificate retrieval modes ───────────────────────────────
        public const string MA_KIEU_LAY_CHUNG_THU_SO_THEO_DANH_SACH = "LAY_DANH_SACH_CHUNG_THU";
        public const string MA_KIEU_LAY_CHUNG_THU_SO_THEO_SERIAL    = "LAY_THEO_SERIAL";

        // ── File types ───────────────────────────────────────────────
        public const string FILE_TYPE_XML  = "XML";
        public const string FILE_TYPE_PDF  = "PDF";
        public const string FILE_TYPE_HASH = "HASH";

        // ── Redis key prefixes ────────────────────────────────────────
        public const string REDIS_PREFIX_TOKEN          = "token_";
        public const string REDIS_PREFIX_SAD            = "sad_";
        public const string REDIS_PREFIX_WAITING        = "waiting:signatures";
        public const string REDIS_PREFIX_VIETTEL_AUTH   = "viettel-auth-state-";
        public const string REDIS_PREFIX_BG_TASK        = "viettel-bg-task-";
        public const string REDIS_PREFIX_EXTEND_TX_LOCK = "viettel-extend-tx-lock-";
    }
}
