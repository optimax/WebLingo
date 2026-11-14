using WebLingo.Bytecode;
namespace WebLingo.Compiler;

/// <summary>A program's classes as a module holds them, the same for both code generators (section 3.7).</summary>
internal static class WebLingoClassTable
{
    public static List<WebLingoClassInfo> Of(WebLingoIrProgram program)
    {
        var classes = new List<WebLingoClassInfo>();
        foreach (var declared in program.Classes)
        {
            var methods = new List<WebLingoClassMethod>();
            foreach (var (name, function) in declared.Methods) methods.Add(new WebLingoClassMethod(name, function));
            var statics = new List<WebLingoClassMethod>();
            foreach (var (name, function) in declared.StaticMethods) statics.Add(new WebLingoClassMethod(name, function));
            classes.Add(new WebLingoClassInfo(declared.Name, declared.Constructor, methods)
            {
                StaticMethods = statics,
                StaticFields = declared.StaticFields,
                DefaultConstructor = declared.DefaultConstructor
            });
        }
        return classes;
    }
}
