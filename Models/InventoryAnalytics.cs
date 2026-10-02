namespace SIRASD.Desktop.Models;

public sealed record InventoryArticle(string Id,string Name,string Unit,decimal Stock,decimal Minimum,string Category);
public sealed record InventoryUse(string ArticleId,DateTime Date,decimal Quantity,bool IsLoss);
public sealed record ConsumptionBar(string Label,decimal Quantity);
public sealed record ConsumptionReport(DateTime Start,DateTime End,decimal Consumed,decimal Losses,int MeasuredDays,decimal DailyRate,IReadOnlyList<ConsumptionBar> Bars);
public static class InventoryAnalytics
{
    public static ConsumptionReport Calculate(string articleId,IEnumerable<InventoryUse> movements,string period,DateTime anchor,DateTime now)
    {
        var start=period switch{"Semana"=>anchor.Date.AddDays(-((int)anchor.DayOfWeek+6)%7),"Mes"=>new DateTime(anchor.Year,anchor.Month,1),"Año"=>new DateTime(anchor.Year,1,1),_=>throw new ArgumentException("Período inválido")};
        var end=period switch{"Semana"=>start.AddDays(7),"Mes"=>start.AddMonths(1),_=>start.AddYears(1)};
        var rows=movements.Where(m=>m.ArticleId==articleId&&m.Date>=start&&m.Date<end&&m.Date<=now).ToList();
        var measuredDays=Math.Max(0,(int)(new[]{end,now.Date.AddDays(1)}.Min()-start).TotalDays);
        var consumed=rows.Where(m=>!m.IsLoss).Sum(m=>m.Quantity);var losses=rows.Where(m=>m.IsLoss).Sum(m=>m.Quantity);
        var bars=new List<ConsumptionBar>();
        for(var day=start;day<end;day=period=="Año"?day.AddMonths(1):day.AddDays(1))
        {
            var next=period=="Año"?day.AddMonths(1):day.AddDays(1);
            bars.Add(new(day.ToString(period=="Año"?"MMM":"dd/MM",System.Globalization.CultureInfo.GetCultureInfo("es-MX")),rows.Where(m=>!m.IsLoss&&m.Date>=day&&m.Date<next).Sum(m=>m.Quantity)));
        }
        return new(start,end,consumed,losses,measuredDays,measuredDays==0?0:consumed/measuredDays,bars);
    }
}
