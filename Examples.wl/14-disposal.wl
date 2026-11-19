// 14-disposal.wl: releasing resources at a known moment, with using and dispose().
//
// Memory is the collector's, but files, connections and locks are released when the program says so: by dispose(),
// or by using, which disposes on every way out of its block, an exception's included. A class may write its
// disposal as dispose() or as destructor(), the same method; callers always call dispose().

class Resource {
  constructor(name) { this.name = name; writeLn($"open {name}"); }
  dispose() { writeLn($"close {this.name}"); }
}

class LoggedResource extends Resource {
  destructor() { writeLn($"flush {this.name}"); }   // runs first, then Resource's dispose(), each once
}

using (var a = new Resource("a")) {
  using (var b = new LoggedResource("b")) {
    writeLn("working with a and b");
  }
}

// using disposes when an exception leaves its block too.
try {
  using (var c = new Resource("c")) {
    throw new Error("something failed");
  }
} catch (e) {
  writeLn($"caught: {e.message}");
}

// Disposing twice does nothing the second time.
var d = new Resource("d");
d.dispose();
d.dispose();

// An async dispose() is awaited by using.
class Connection {
  async dispose() { await delay(1); writeLn("connection closed"); }
}
using (var connection = new Connection()) {
  writeLn("query");
}
writeLn("after the connection");

/* Output:
open a
open b
working with a and b
flush b
close b
close a
open c
close c
caught: something failed
open d
close d
query
connection closed
after the connection
*/
