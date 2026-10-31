using System.Reflection;
using Jellyfin.Plugin.JuxHomepage.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.JuxHomepage.Tests.Controllers;

// The HTTP contract of the plugin: every action of every controller in the assembly, as verb, full route,
// authorization, return type and declared status codes. The table is read from the assembly, not from one
// controller type, so moving an action between controllers leaves this file unchanged; any other change to
// a route, its policy or its answer shows here as a diff to review.
public sealed class RouteContractTests
{
    private static readonly string[] ExpectedContract =
    [
        "GET | JuxHomepage/Admin/Catalog |  | RequiresElevation | ActionResult<WidgetCatalog> | 200",
        "GET | JuxHomepage/Admin/Warnings | coexistenceWarnings:FromServices | RequiresElevation | ActionResult<IReadOnlyList<String>> | 200",
        "GET | JuxHomepage/Collections/IncludedIn/{itemId} | itemId:FromRoute | authenticated | Task<ActionResult<IReadOnlyList<CollectionRef>>> | 200",
        "GET | JuxHomepage/Localizations | lang:FromQuery | anonymous | ActionResult<IReadOnlyDictionary<String,String>> | 200",
        "GET | JuxHomepage/MovieHistory/Items | userId:FromQuery,sortBy:FromQuery,sortOrder:FromQuery,searchTerm:FromQuery,startIndex:FromQuery,limit:FromQuery | authenticated | Task<ActionResult<WidgetResult>> | 200,403",
        "GET | JuxHomepage/PatchStatus | patchStatus:FromServices | authenticated | ActionResult<PatchStatusView> | 200",
        "GET | JuxHomepage/Rewards/Search/Category | query:FromQuery | RequiresElevation | Task<ActionResult<IReadOnlyList<AdminWidgetValue>>> | 200",
        "GET | JuxHomepage/Rewards/Search/Ceremony | query:FromQuery | RequiresElevation | Task<ActionResult<IReadOnlyList<AdminWidgetValue>>> | 200",
        "GET | JuxHomepage/Section/{widgetType} | widgetType:FromRoute,userId:FromQuery,additionalData:FromQuery,sectionKey:FromQuery,startIndex:FromQuery,limit:FromQuery | authenticated | Task<ActionResult<WidgetResult>> | 200,403,404",
        "GET | JuxHomepage/Sections | userId:FromQuery,page:FromQuery,lang:FromQuery | authenticated | Task<ActionResult<SectionsPage>> | 200,403",
        "GET | JuxHomepage/SeriesProgress/Items | userId:FromQuery,sortBy:FromQuery,sortOrder:FromQuery,searchTerm:FromQuery,startIndex:FromQuery,limit:FromQuery | authenticated | Task<ActionResult<SeriesProgressResult>> | 200,403",
        "GET | JuxHomepage/Statistics | userId:FromQuery | authenticated | Task<ActionResult<WatchingStatistics>> | 200,403",
        "GET | JuxHomepage/TMDb/Countries |  | RequiresElevation | Task<ActionResult<IReadOnlyList<AdminWidgetValue>>> | 200",
        "GET | JuxHomepage/TMDb/Genres |  | RequiresElevation | Task<ActionResult<IReadOnlyList<AdminWidgetValue>>> | 200",
        "GET | JuxHomepage/TMDb/Search/Company | query:FromQuery | RequiresElevation | Task<ActionResult<IReadOnlyList<AdminWidgetValue>>> | 200",
        "GET | JuxHomepage/TMDb/Search/Keyword | query:FromQuery | RequiresElevation | Task<ActionResult<IReadOnlyList<AdminWidgetValue>>> | 200",
        "GET | JuxHomepage/TMDb/Search/Person | query:FromQuery | RequiresElevation | Task<ActionResult<IReadOnlyList<AdminWidgetValue>>> | 200",
        "GET | JuxHomepage/TMDb/Status |  | RequiresElevation | ActionResult<IReadOnlyList<TMDbCacheStatusDto>> | 200",
        "GET | JuxHomepage/UserSettings | userId:FromQuery,lang:FromQuery | authenticated | Task<ActionResult<UserSettingsView>> | 200,403",
        "GET | JuxHomepage/Watchlist/Ids | userId:FromQuery | authenticated | Task<ActionResult<WatchlistIdsResult>> | 200,403",
        "GET | JuxHomepage/Watchlist/Items | userId:FromQuery,sortBy:FromQuery,sortOrder:FromQuery,includeItemTypes:FromQuery,searchTerm:FromQuery,startIndex:FromQuery,limit:FromQuery | authenticated | Task<ActionResult<WidgetResult>> | 200,403",
        "GET | JuxHomepage/Widget/{widgetType}/values | widgetType:FromRoute,userId:FromQuery,search:FromQuery | RequiresElevation | ActionResult<IReadOnlyList<AdminWidgetValue>> | 200,400,404",
        "GET | JuxHomepage/{fileName} | fileName:FromRoute | anonymous | IActionResult | 200,404",
        "POST | JuxHomepage/Admin/RestoreConfiguration | backup:FromServices | RequiresElevation | ActionResult | 204,400,404",
        "POST | JuxHomepage/Preview | row:FromBody,resolver:FromServices,userId:FromQuery,lang:FromQuery | RequiresElevation | Task<ActionResult<WidgetPreview>> | 200,403,404,500",
        "POST | JuxHomepage/TMDb/Refresh |  | RequiresElevation | IActionResult | 202,409",
        "POST | JuxHomepage/UserSettings | userId:FromQuery,update:FromBody | authenticated | Task<ActionResult> | 204,403,500",
    ];

    [Fact]
    public void Routes_MatchTheFrozenContract()
    {
        Assert.Equal(ExpectedContract, DescribeActions());
    }

    [Fact]
    public void Routes_AreAllDiscovered()
    {
        // Guards against the reflection probe above silently describing nothing.
        Assert.Equal(27, DescribeActions().Count);
    }

    private static List<string> DescribeActions() =>
        typeof(JuxHomepageController).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Select(m => Describe(t, m)))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string Describe(Type controller, MethodInfo action)
    {
        var verb = action.GetCustomAttribute<HttpMethodAttribute>()!;
        var classTemplate = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
        var controllerName = controller.Name.EndsWith("Controller", StringComparison.Ordinal)
            ? controller.Name[..^"Controller".Length]
            : controller.Name;
        var route = classTemplate.Replace("[controller]", controllerName, StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(verb.Template))
        {
            route += "/" + verb.Template;
        }

        var codes = action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(p => p.StatusCode)
            .Order()
            .Select(c => c.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return string.Join(
            " | ",
            string.Join(",", verb.HttpMethods),
            route,
            Parameters(action),
            Authorization(controller, action),
            FriendlyName(action.ReturnType),
            string.Join(",", codes));
    }

    private static string Parameters(MethodInfo action) =>
        string.Join(
            ",",
            action.GetParameters()
                .Where(p => p.ParameterType != typeof(CancellationToken))
                .Select(p => $"{p.Name}:{Binding(p)}"));

    private static string Binding(ParameterInfo parameter) =>
        parameter.GetCustomAttributes()
            .Select(a => a.GetType().Name)
            .FirstOrDefault(n => n.StartsWith("From", StringComparison.Ordinal))?[..^"Attribute".Length]
        ?? "Default";

    private static string Authorization(Type controller, MethodInfo action)
    {
        if (action.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
        {
            return "anonymous";
        }

        var policies = action.GetCustomAttributes<AuthorizeAttribute>()
            .Concat(controller.GetCustomAttributes<AuthorizeAttribute>())
            .Select(a => a.Policy ?? "authenticated")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        return policies.Count == 0 ? "none" : string.Join("+", policies);
    }

    private static string FriendlyName(Type type) =>
        type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(",", type.GetGenericArguments().Select(FriendlyName))}>"
            : type.Name;
}
