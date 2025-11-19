using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using L2InGameVision.OneOcr.Models;
using static L2InGameVision.OneOcr.OneOcrWinApi;

namespace OneOCRConsole.OneOcr
{
    /// <summary>
    /// Thin managed wrapper around the native OneOCR pipeline.
    /// Responsible for ensuring required native assets exist, initializing a reusable pipeline,
    /// and converting .NET bitmaps into the native image layout expected by the DLL.
    /// </summary>
    internal class OneOcrEngine
    {
        // The model key is required by OneOCR to open the onemodel file.
        // Keep it in sync with the model bundled next to the application.
        private const string ModelKey = "kj)TGtrK>f]b[Piow.gU+nC@s\"\"\"\"\"\"4";

        // OneOCR model path (copied next to the executable by OneOcrBootstrap).
        private static readonly string ModelFilePath = Path.Combine(AppContext.BaseDirectory, "oneocr.onemodel");

        // Native handles/contexts (created once and reused for the app lifetime).
        private readonly static long _pipeline;
        private readonly static long _processOptions;

        // Static constructor performs one-time initialization of the native pipeline.
        static OneOcrEngine()
        {
            try
            {
                // Create init options and configure them.
                // Note: these native APIs return 0 on success; non-zero indicates failure.
                CreateOcrInitOptions(out long ctx);
                OcrInitOptionsSetUseModelDelayLoad(ctx, 0); // 0 = load model immediately

                // Create the reusable pipeline and process options.
                CreateOcrPipeline(ModelFilePath, ModelKey, 0, out _pipeline);
                CreateOcrProcessOptions(out _processOptions);
                OcrProcessOptionsSetMaxRecognitionLineCount(_processOptions, 1000); // guardrail to avoid runaway output
            }
            catch (DllNotFoundException ex)
            {
                // If the native DLL cannot be loaded, surface a clear message to the caller.
                throw new InvalidOperationException(
                    "OCR initialization failed. Ensure oneocr.dll, oneocr.onemodel, and onnxruntime.dll files are in the application directory.",
                    ex);
            }
        }

        /// <summary>
        /// Runs OCR over the provided bitmap and returns recognized lines and words.
        /// The bitmap is converted to 32bpp ARGB and pinned for the duration of the native call.
        /// </summary>
        public static OcrResult Recognize(Bitmap bitmap)
        {
            ArgumentNullException.ThrowIfNull(bitmap);

            // Normalize to 32bpp ARGB (the format expected by OneOCR).
            using Bitmap img32 = ConvertTo32bppArgb(bitmap);

            // Lock pixel data for zero-copy handoff to native code.
            Rectangle rect = new(0, 0, img32.Width, img32.Height);
            BitmapData bitmapData = img32.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride = Math.Abs(bitmapData.Stride);

                // Img mirrors the native structure layout expected by the DLL.
                // t = 3 corresponds to 32bpp ARGB in reverse-engineered API usage.
                Img imgStruct = new()
                {
                    t = 3,
                    col = img32.Width,
                    row = img32.Height,
                    _unk = 0,
                    step = stride,
                    data_ptr = bitmapData.Scan0
                };

                // Run the pipeline and obtain a per-call instance/handle with results.
                RunOcrPipeline(_pipeline, ref imgStruct, _processOptions, out long instance);

                // Query number of lines and pre-size the managed collection to reduce reallocations.
                GetOcrLineCount(instance, out long lineCount);

                var lines = new List<OcrLine>((int)Math.Min(lineCount, int.MaxValue));

                for (long i = 0; i < lineCount; i++)
                {
                    // Skip if the native call fails or returns an invalid handle.
                    if (!TryGetLine(instance, i, out long lineHandle))
                        continue;

                    // Extract managed-friendly data (text, bounding box, words) for the line.
                    OcrLine? lineData = ExtractLineData(lineHandle);
                    if (lineData is not null)
                        lines.Add(lineData);
                }

                return new OcrResult { Lines = lines };
            }
            finally
            {
                // Always release the bitmap lock to avoid resource leaks.
                img32.UnlockBits(bitmapData);
            }
        }

        /// <summary>
        /// Helper that validates native result status and non-zero line handle.
        /// </summary>
        private static bool TryGetLine(long instance, long index, out long lineHandle) =>
            GetOcrLine(instance, index, out lineHandle) == 0 && lineHandle != 0;

        /// <summary>
        /// Helper that validates native result status and non-zero word handle.
        /// </summary>
        private static bool TryGetWord(long lineHandle, long index, out long wordHandle) =>
            GetOcrWord(lineHandle, index, out wordHandle) == 0 && wordHandle != 0;

        /// <summary>
        /// Ensures the image is 32bpp ARGB. Clones if already in the desired format, otherwise redraws.
        /// </summary>
        private static Bitmap ConvertTo32bppArgb(Bitmap source)
        {
            if (source.PixelFormat == PixelFormat.Format32bppArgb)
                return (Bitmap)source.Clone();

            Bitmap converted = new(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(converted))
            {
                g.DrawImage(source, 0, 0, source.Width, source.Height);
            }
            return converted;
        }

        /// <summary>
        /// Reads a line's text and bounding box; returns null when native data is unavailable.
        /// </summary>
        private static OcrLine? ExtractLineData(long lineHandle)
        {
            // Content pointer is UTF-8; null/zero indicates failure.
            if (GetOcrLineContent(lineHandle, out IntPtr contentPtr) != 0 || contentPtr == IntPtr.Zero)
                return null;

            string lineText = MarshalPtrToString(contentPtr);

            // Bounding box is a struct returned by the DLL; null/zero indicates failure.
            if (GetOcrLineBoundingBox(lineHandle, out IntPtr boxPtr) != 0 || boxPtr == IntPtr.Zero)
                return null;

            BoundingBox boundingBox = Marshal.PtrToStructure<BoundingBox>(boxPtr);

            return new OcrLine
            {
                Text = lineText,
                X1 = boundingBox.x1,
                Y1 = boundingBox.y1,
                X2 = boundingBox.x2,
                Y2 = boundingBox.y2,
                X3 = boundingBox.x3,
                Y3 = boundingBox.y3,
                X4 = boundingBox.x4,
                Y4 = boundingBox.y4,
                Words = ExtractWords(lineHandle)
            };
        }

        /// <summary>
        /// Enumerates words for a line. Returns an empty array when the line has no words or extraction fails.
        /// </summary>
        private static OcrWord[] ExtractWords(long lineHandle)
        {
            // Query word count up front to size the list appropriately.
            GetOcrLineWordCount(lineHandle, out long wordCount);
            if (wordCount == 0)
                return Array.Empty<OcrWord>();

            var words = new List<OcrWord>((int)Math.Min(wordCount, int.MaxValue));

            for (long i = 0; i < wordCount; i++)
            {
                if (!TryGetWord(lineHandle, i, out long wordHandle))
                    continue;

                OcrWord? word = ExtractWordData(wordHandle);
                if (word is not null)
                    words.Add(word);
            }

            return words.Count == 0 ? [] : words.ToArray();
        }

        /// <summary>
        /// Reads a word's text and bounding box; returns null when native data is unavailable.
        /// </summary>
        private static OcrWord? ExtractWordData(long wordHandle)
        {
            if (GetOcrWordContent(wordHandle, out IntPtr contentPtr) != 0 || contentPtr == IntPtr.Zero)
                return null;

            string wordText = MarshalPtrToString(contentPtr);

            if (GetOcrWordBoundingBox(wordHandle, out IntPtr boxPtr) != 0 || boxPtr == IntPtr.Zero)
                return null;

            BoundingBox boundingBox = Marshal.PtrToStructure<BoundingBox>(boxPtr);

            return new OcrWord
            {
                Text = wordText,
                X1 = boundingBox.x1,
                Y1 = boundingBox.y1,
                X2 = boundingBox.x2,
                Y2 = boundingBox.y2,
                X3 = boundingBox.x3,
                Y3 = boundingBox.y3,
                X4 = boundingBox.x4,
                Y4 = boundingBox.y4
            };
        }

        /// <summary>
        /// Converts a UTF-8 pointer returned by native code to a managed string.
        /// </summary>
        private static string MarshalPtrToString(IntPtr ptr) =>
            ptr == IntPtr.Zero ? string.Empty : (Marshal.PtrToStringUTF8(ptr) ?? string.Empty);
    }
}