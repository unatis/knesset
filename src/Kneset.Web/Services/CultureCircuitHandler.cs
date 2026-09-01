using System.Globalization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Kneset.Web.Services;

/// <summary>
/// Переносит выбранный язык из HTTP-запроса в цепь Blazor.
///
/// UseRequestLocalization выставляет культуру на время обработки запроса.
/// Предварительная отрисовка идёт внутри запроса и язык получает; всё, что
/// компонент отрисовывает позже — по нажатию, при обновлении данных, — идёт
/// уже в цепи SignalR, вне того запроса, и культура там сбрасывается
/// к серверной по умолчанию.
///
/// Со стороны это выглядело так: заголовок вкладки и меню на иврите,
/// а тело страницы по-русски, причём из одного и того же ключа ресурсов.
/// Разница была ровно между статической отрисовкой и интерактивной.
///
/// Обработчик запоминает культуру в момент создания цепи — тогда ещё
/// действует прослойка локализации, и в куке уже лежит выбор человека —
/// и возвращает её перед каждой порцией работы внутри цепи.
/// </summary>
public sealed class CultureCircuitHandler(CultureInfo culture, CultureInfo uiCulture) : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next) =>
        async context =>
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
            await next(context);
        };
}
