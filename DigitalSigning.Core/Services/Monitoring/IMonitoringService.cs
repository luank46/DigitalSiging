using System.Collections.Generic;
using System.Threading.Tasks;

namespace DigitalSigning.Core.Services.Monitoring
{
    /// <summary>
    /// Interface for external monitoring/metrics services (InfluxDB, QILogger).
    /// Mapping từ IMonitoringService cũ.
    /// </summary>
    public interface IMonitoringService
    {
        /// <summary>Ghi metric theo action + thời gian.</summary>
        Task WriteTimeMetric(string metrics, string action, long time);

        /// <summary>Ghi danh sách metric dạng line protocol.</summary>
        Task WriteTimeMetricByString(List<string> metrics);

        /// <summary>Ghi dữ liệu thô.</summary>
        void Write(string data);
    }
}
