using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FamilyHQ.E2E.Data.Models;
using Reqnroll;

namespace FamilyHQ.E2E.Steps.Hooks;

[Binding]
public class TemplateHooks
{
    public static Dictionary<string, SimulatorConfigurationModel> UserTemplates { get; private set; } = new();

    [BeforeTestRun]
    public static void LoadTemplates()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Templates", "user_templates.json");
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            UserTemplates = JsonSerializer.Deserialize<Dictionary<string, SimulatorConfigurationModel>>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
    }
}
