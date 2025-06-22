using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace BrowserReporterService.Services
{
    public class ScreenshotService
    {
        private readonly Serilog.ILogger _logger;

        public ScreenshotService(Serilog.ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Captures the current primary screen and returns PNG bytes.
        /// </summary>
        public byte[] CaptureScreenshotPng()
        {
            try
            {
                var bounds = Screen.PrimaryScreen.Bounds;
                using var bitmap = new Bitmap(bounds.Width, bounds.Height);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                }
                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Png);
                _logger.Information("Screenshot captured. Size: {Bytes} bytes", ms.Length);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to capture screenshot.");
                throw;
            }
        }
    }
} 