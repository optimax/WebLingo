namespace WebLingo.Core;

/// <summary>
/// What a native class's member gives, which only calls its built-in with its parameters (specification, section 3.9):
/// the built-in's result, a method's and a getter's; its last argument, the value a setter assigns; or nothing, a
/// constructor's, whose <c>new</c> gives the instance.
/// </summary>
public enum WebLingoNativeResult : byte { Result, LastArgument, Nothing }
