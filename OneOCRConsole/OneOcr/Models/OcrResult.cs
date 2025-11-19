using System.Collections.Generic;
using System.Linq;

namespace L2InGameVision.OneOcr.Models
{
    internal class OcrResult
    {
        public IReadOnlyList<OcrLine> Lines { get; init; } = [];
        public string Text => string.Join('\n', Lines.Select(line => line.Text));
    }
}
