using Microsoft.AspNetCore.Mvc;
using TriviaBattle.Server.Data;
using TriviaBattle.Server.Middleware;

namespace TriviaBattle.Server.Admin;

/// <summary>
/// Question pack import/export and media upload for the admin page.
///   GET  /api/admin/transfer/export.csv | export.json   download every question
///   POST /api/admin/transfer/import                     upload a .csv or .json pack
///   POST /api/admin/transfer/media                      upload an image/video for a question
/// </summary>
[ApiController]
[Route("api/admin/transfer")]
[AdminApiKey]
public class AdminTransferController(TriviaDbContext db, IWebHostEnvironment env) : ControllerBase
{
    private const long MaxMediaBytes = 50 * 1024 * 1024;
    private static readonly string[] AllowedMediaTypes = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".mp4", ".webm"];

    [HttpGet("export.csv")]
    public async Task<IActionResult> ExportCsv() =>
        File(await new QuestionTransfer(db).ExportCsvAsync(), "text/csv", $"trivia-questions-{DateTime.Now:yyyy-MM-dd}.csv");

    [HttpGet("export.json")]
    public async Task<IActionResult> ExportJson() =>
        File(System.Text.Encoding.UTF8.GetBytes(await new QuestionTransfer(db).ExportJsonAsync()), "application/json", $"trivia-questions-{DateTime.Now:yyyy-MM-dd}.json");

    [HttpPost("import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Import(IFormFile file)
    {
        List<QuestionRow> rows;
        try
        {
            await using var stream = file.OpenReadStream();
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            rows = extension switch
            {
                ".csv" => QuestionTransfer.ParseCsv(stream),
                ".json" => QuestionTransfer.ParseJson(await new StreamReader(stream).ReadToEndAsync()),
                _ => throw new InvalidOperationException("Please upload a .csv or .json file."),
            };
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            return BadRequest(new { problems = new[] { $"Couldn't read the file: {ex.Message}" } });
        }

        if (rows.Count == 0)
            return BadRequest(new { problems = new[] { "The file has no questions in it." } });

        var result = await new QuestionTransfer(db).ImportAsync(rows);
        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    /// <summary>Saves an image or video under /media/questions/ and returns its URL for the question's MediaUrl.</summary>
    [HttpPost("media")]
    [RequestSizeLimit(MaxMediaBytes)]
    public async Task<IActionResult> UploadMedia(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedMediaTypes.Contains(extension))
            return BadRequest(new { problems = new[] { $"Allowed file types: {string.Join(", ", AllowedMediaTypes)}." } });

        var folder = Path.Combine(env.WebRootPath, "media", "questions");
        Directory.CreateDirectory(folder);

        // A random name, so uploads can't overwrite each other or escape the folder.
        var fileName = $"{Guid.NewGuid():N}{extension}";
        await using (var target = System.IO.File.Create(Path.Combine(folder, fileName)))
            await file.CopyToAsync(target);

        return Ok(new { url = $"/media/questions/{fileName}" });
    }
}
