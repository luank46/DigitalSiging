using System;
using System.Collections.Generic;

namespace DigitalSigning.Core.Settings
{
    public class MongoDbSettings
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = "digital_signing";
        public string TransactionCollection { get; set; } = "transactions";
        public string EventCollection { get; set; } = "transaction_events";
        public string WebhookCollection { get; set; } = "webhook_deliveries";
        public string IdempotencyCollection { get; set; } = "idempotency_keys";
        public int MaxPoolSize { get; set; } = 500;
    }

    public class RabbitMqSettings
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5672;
        public string UserName { get; set; } = "guest";
        public string Password { get; set; } = "guest";
        public string VirtualHost { get; set; } = "/";
    }

    public class RedisSettings
    {
        public string ConnectionString { get; set; } = "localhost:6379";
        public string InstanceName { get; set; } = "RemoteSigning_";
    }

    /// <summary>
    /// CA provider API endpoint configuration.
    /// Mỗi provider có thể có các URL riêng: auth, sign, certificate, polling, etc.
    /// </summary>
    /// <summary>File upload URL config theo năm học</summary>
    public class FileConfig
    {
        public int MaNamHoc { get; set; }
        public string? UrlUploadFile { get; set; }
        public string? UrlViewFile { get; set; }
    }

    public class ProviderSettings
    {
        /// <summary>VNPT SmartCA API</summary>
        public string VnptBaseUrl { get; set; } = "https://gwsca.vnpt.vn";
        public string VnptTokenUrl { get; set; } = "https://gwsca.vnpt.vn/auth/token";
        public string VnptCredentialListUrl { get; set; } = "https://gwsca.vnpt.vn/csc/credentials/list";
        public string VnptCredentialInfoUrl { get; set; } = "https://gwsca.vnpt.vn/csc/credentials/info";
        public string VnptSignHashUrl { get; set; } = "https://gwsca.vnpt.vn/csc/signature/signhash";
        public string VnptTransactionInfoUrl { get; set; } = "https://gwsca.vnpt.vn/csc/credentials/gettraninfo";
        public string VnptClientId { get; set; } = "dev-placeholder";
        public string VnptClientSecret { get; set; } = "dev-placeholder";

        /// <summary>Viettel CA API</summary>
        public string ViettelTokenUrl { get; set; } = "https://remotesigning.viettel.vn:8773/auth/realms/viettelsmartca/protocol/openid-connect/token";
        public string ViettelAuthorizeUrl { get; set; } = "https://remotesigning.viettel.vn:8773/auth/realms/viettelsmartca/protocol/openid-connect/auth";
        public string ViettelCscBaseUrl { get; set; } = "https://remotesigning.viettel.vn:8773/csc/v1/";
        public string ViettelClientId { get; set; } = "dev-placeholder";
        public string ViettelClientSecret { get; set; } = "dev-placeholder";

        /// <summary>BKAV CA API</summary>
        public string BkavBaseUrl { get; set; } = "https://idp.bkavca.vn:443";
        public string BkavBaseUrlAuthen { get; set; } = "https://10.88.44.186";
        public string BkavBaseUrlSign { get; set; } = "https://sca.bkavca.vn:443";
        public string BkavLoginUrl { get; set; } = "https://idp.bkavca.vn:443/api/auth/login";
        public string BkavCertListUrl { get; set; } = "https://idp.bkavca.vn:443/api/certificate/list";
        public string BkavSignUrl { get; set; } = "https://sca.bkavca.vn:443/api/sign/hash";
        public string BkavTransactionUrl { get; set; } = "https://sca.bkavca.vn:443/api/sign/transaction";
        public string BkavAppId { get; set; } = "dev-placeholder";
        public string BkavClientId { get; set; } = "dev-placeholder";
        public string BkavClientSecret { get; set; } = "dev-placeholder";

        /// <summary>GCC HSM API</summary>
        public string GccBaseUrl { get; set; } = "https://mpki2.ca.gov.vn/mpki/v2";
        public string GccTokenUrl { get; set; } = "https://mpki2.ca.gov.vn/mpki/v2/auth/token";
        public string GccCertUrl { get; set; } = "https://mpki2.ca.gov.vn/mpki/v2/certificate";
        public string GccSignUrl { get; set; } = "https://mpki2.ca.gov.vn/mpki/v2/sign";
        public string GccTransactionUrl { get; set; } = "https://mpki2.ca.gov.vn/mpki/v2/transaction";
        public string GccApiKey { get; set; } = "";
        public string GccApiSecret { get; set; } = "";

        /// <summary>MISA-CA API</summary>
        public string MisaBaseUrl { get; set; } = "https://esignapp.misa.vn";
        public string MisaLoginUrl { get; set; } = "https://esignapp.misa.vn/api/v1/auth/login";
        public string MisaCertListUrl { get; set; } = "https://esignapp.misa.vn/api/v1/certificates";
        public string MisaSignUrl { get; set; } = "https://esignapp.misa.vn/api/v1/sign/hash";
        public string MisaTransactionUrl { get; set; } = "https://esignapp.misa.vn/api/v1/transactions";
        public string MisaAppId { get; set; } = "dev-placeholder";
        public string MisaAppSecret { get; set; } = "dev-placeholder";
    }

    public class AppSettings
    {
        public MongoDbSettings MongoDB { get; set; } = new();
        public RabbitMqSettings RabbitMQ { get; set; } = new();
        public RedisSettings Redis { get; set; } = new();
        public string ServiceName { get; set; } = "DigitalSigningImproved";
        public List<FileConfig> FileConfigs { get; set; } = new();

        /// <summary>Allowed API keys for authentication</summary>
        public string ApiKeys { get; set; } = "dev-placeholder";
        /// <summary>API key for file upload service</summary>
        public string UploadApiKey { get; set; } = "dev-placeholder";

        /// <summary>Feature flags for toggling new behaviors.</summary>
        public FeatureFlags FeatureFlags { get; set; } = new();
    }

    /// <summary>
    /// Feature flags for gradual rollout of new capabilities.
    /// Each flag can also be overridden per-tenant via a tenant-level configuration store.
    /// </summary>
    public class FeatureFlags
    {
        /// <summary>
        /// When true, the new idempotency V2 logic (lookup existing transaction by key,
        /// race-condition handling) is enabled. When false, fall back to the original
        /// behavior (proceed with new transaction after detecting duplicate).
        /// </summary>
        public bool IdempotencyV2 { get; set; } = true;
    }
}