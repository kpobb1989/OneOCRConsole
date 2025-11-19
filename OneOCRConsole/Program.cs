using OneOCRConsole.OneOcr;

var srcPath =  $"{AppDomain.CurrentDomain.BaseDirectory}\\src";
var destPath = $"{AppDomain.CurrentDomain.BaseDirectory}\\dest";
var allowedExt = new[] { ".png", ".jpg", ".jpeg", ".bmp" };

Console.WriteLine("OneOCR Console Application");

try
{
    if (!Directory.Exists(srcPath))
    {
        Directory.CreateDirectory(srcPath);
        Console.WriteLine($"Source directory created at: {srcPath}");
    }

    if (!Directory.Exists(destPath))
    {
        Directory.CreateDirectory(destPath);
        Console.WriteLine($"Destination directory created at: {destPath}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error creating directories: {ex.Message}");
    return;
}

string[] imageFiles;

try
{
    imageFiles = Directory.EnumerateFiles(srcPath)
        .Where(f => allowedExt.Contains(Path.GetExtension(f).ToLowerInvariant()))
        .ToArray();

    Console.WriteLine($"Found {imageFiles.Length} image(s) to process (supported: {string.Join(", ", allowedExt)}).");

    if (imageFiles.Length == 0)
    {
        Console.WriteLine("Please add PNG/JPEG/BMP images to the source directory and rerun the application.");
        return;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error accessing source directory: {ex.Message}");
    return;
}

try
{
    foreach (var imageFile in imageFiles)
    {
        using var fs = File.OpenRead(imageFile);
        using Bitmap bmp = new(fs);

        var result = OneOcrEngine.Recognize(bmp);

        File.WriteAllText(Path.Combine(destPath, Path.GetFileNameWithoutExtension(imageFile) + ".txt"), result.Text);

        Console.WriteLine($"Processed image: {imageFile}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error processing images: {ex.Message}");
}

Console.WriteLine("Done. Press any key to exit.");

Console.ReadKey();