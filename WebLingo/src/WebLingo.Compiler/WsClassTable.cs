using WebLingo.Bytecode;
namespace WebLingo.Compiler;

/// <summary>A program's classes as a module holds them, the same for both code generators (section 3.7).</summary>
internal static class WsClassTable
{
    public static List<WsClassInfo> Of(WsIrProgram program)
    {
        var classes = new List<WsClassInfo>();
        foreach (var declared in program.Classes)
        {
            var methods = new List<WsClassMethod>();
            foreach (var (name, function) in declared.Methods) methods.Add(new WsClassMethod(name, function));
            var statics = new List<WsClassMethod>();
            foreach (var (name, function) in declared.StaticMethods) statics.Add(new WsClassMethod(name, function));
            classes.Add(new WsClassInfo(declared.Name, declared.Constructor, methods)
            {
                StaticMethods = statics,
                StaticFields = declared.StaticFields
            });
        }
        return classes;
    }
}
