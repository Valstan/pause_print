using System.Runtime.Serialization;

namespace PausePrint.Interop;

public record PrintJobDto(string PrinterName, uint JobId, string UserName, string DocumentName);

public enum JobDecision
{
    Accept,
    Hold,
    Cancel
}


