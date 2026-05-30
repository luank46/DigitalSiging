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
        public string VnptClientId { get; set; } = "4eea-637890861718114234.apps.smartcaapi.com";
        public string VnptClientSecret { get; set; } = "YjNmNmIwY2Y-YzcyMS00ZWVh";

        /// <summary>Viettel CA API</summary>
        public string ViettelTokenUrl { get; set; } = "https://remotesigning.viettel.vn:8773/auth/realms/viettelsmartca/protocol/openid-connect/token";
        public string ViettelAuthorizeUrl { get; set; } = "https://remotesigning.viettel.vn:8773/auth/realms/viettelsmartca/protocol/openid-connect/auth";
        public string ViettelCscBaseUrl { get; set; } = "https://remotesigning.viettel.vn:8773/csc/v1/";
        public string ViettelClientId { get; set; } = "qlnt_quangich";
        public string ViettelClientSecret { get; set; } = "e69b1f796f92d0c35559c69cdf1c3ba3ead5f07a";

        /// <summary>BKAV CA API</summary>
        public string BkavBaseUrl { get; set; } = "https://idp.bkavca.vn:443";
        public string BkavBaseUrlAuthen { get; set; } = "https://10.88.44.186";
        public string BkavBaseUrlSign { get; set; } = "https://sca.bkavca.vn:443";
        public string BkavLoginUrl { get; set; } = "https://idp.bkavca.vn:443/api/auth/login";
        public string BkavCertListUrl { get; set; } = "https://idp.bkavca.vn:443/api/certificate/list";
        public string BkavSignUrl { get; set; } = "https://sca.bkavca.vn:443/api/sign/hash";
        public string BkavTransactionUrl { get; set; } = "https://sca.bkavca.vn:443/api/sign/transaction";
        public string BkavAppId { get; set; } = "eNetViet";
        public string BkavClientId { get; set; } = "sp1";
        public string BkavClientSecret { get; set; } = "49OcnU1MzES4";

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
        public string MisaAppId { get; set; } = "25f73099-fe53-4477-9b5c-53c89d8be20c";
        public string MisaAppSecret { get; set; } = "63779c40-af83-41ee-8017-2ef715e5ff89";
    }

    public class AppSettings
    {
        public MongoDbSettings MongoDB { get; set; } = new();
        public RabbitMqSettings RabbitMQ { get; set; } = new();
        public RedisSettings Redis { get; set; } = new();
        public string ServiceName { get; set; } = "DigitalSigningImproved";
        public List<FileConfig> FileConfigs { get; set; } = new();
    }
}