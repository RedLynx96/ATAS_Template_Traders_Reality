using System;
using System.Linq;
using System.Reflection;
using ATAS.Indicators;

var asm = Assembly.LoadFrom(@"C:\Users\david\AppData\Roaming\ATAS\Indicators\CustomIndicatorsEma.dll");
foreach (var t in asm.GetTypes().Where(t => typeof(Indicator).IsAssignableFrom(t)))
{
    var dn = t.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType.Name.Contains("DisplayName"));
    var display = dn?.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? t.Name;
    Console.WriteLine($"{display} => {t.FullName}");
}
