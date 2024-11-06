using CodeGenerator.Models;
using Microsoft.AspNetCore.Mvc;
using SqlGenerator.Stored_Procedures;
using System.Diagnostics;

namespace CodeGenerator.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }
        [HttpPost]
        public JsonResult GenerateViews(ViewGeneratorViewModel model)
        {
            if (string.IsNullOrEmpty(model.ConnectionString))
            {
                // Returning a JSON response with an error message when the connection string is missing
                return Json(new { success = false, message = "Connection string is required." });
            }

            try
            {
                var generator = new ViewGenerator(model.ConnectionString);
                model.GeneratedSQL = generator.GenerateViewsForForeignKeys();

                // Returning the generated SQL in JSON format
                return Json(new { success = true, generatedSQL = model.GeneratedSQL });
            }
            catch (Exception ex)
            {
                // Returning a JSON response with an error message in case of an exception
                return Json(new { success = false, message = $"Error generating views: {ex.Message}" });
            }
        }
        [HttpPost]
        public JsonResult GenerateProcs(ViewGeneratorViewModel model)
        {
            if (string.IsNullOrEmpty(model.ConnectionString))
            {
                // Returning a JSON response with an error message when the connection string is missing
                return Json(new { success = false, message = "Connection string is required." });
            }

            try
            {
                var generator = new StoredProcedureGenerator(model.ConnectionString);
                model.GeneratedSQL = generator.GenerateStoredProceduresForAllTables();

                // Returning the generated SQL in JSON format
                return Json(new { success = true, generatedSQL = model.GeneratedSQL });
            }
            catch (Exception ex)
            {
                // Returning a JSON response with an error message in case of an exception
                return Json(new { success = false, message = $"Error generating views: {ex.Message}" });
            }
        }
        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
    public class ViewGeneratorViewModel
    {
        public string ConnectionString { get; set; }
        public string GeneratedSQL { get; set; }
    }
}
