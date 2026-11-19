// misspelt-parameter.wl: a module's parameters are checked where it is imported.
// A misspelt name, or one the module does not declare, is an error before the script runs, not a value ignored.

import teas from "../lib/teas.wl" { curency: "CHF" };

/* Errors:
misspelt-parameter.wl(4,37): error WL2016: 'curency' is not a parameter of "../lib/teas.wl".
*/
