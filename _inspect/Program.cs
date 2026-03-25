using System;
using ATAS.Indicators;

var s = new ValueDataSeries("x");
Console.WriteLine($"Color={s.Color}");
Console.WriteLine($"RenderColor={s.RenderColor}");
Console.WriteLine($"ValuesColor={s.ValuesColor}");
Console.WriteLine($"Width={s.Width}");
Console.WriteLine($"VisualType={s.VisualType}");
