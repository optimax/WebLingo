// 22-environment.wl: the machine's environment, with std:env.
//
// A script under weblingo reads the machine's name and its environment variables, and sets variables for its own
// process. A site under wasp reads only what its wasp.config allows, and sets nothing: an EnvironmentPermissionError.

import { Environment } from "std:env";

Environment.setVariable("TEA_OF_THE_DAY", "genmaicha");
writeLn([Environment.variable("TEA_OF_THE_DAY"), Environment.variables["TEA_OF_THE_DAY"]]);
Environment.setVariable("TEA_OF_THE_DAY", null);
writeLn([Environment.variable("TEA_OF_THE_DAY"), Environment.variable("NO_SUCH_VARIABLE_HERE")]);
writeLn([Environment.machineName is String, Environment.machineName.length > 0]);

/* Output:
["genmaicha", "genmaicha"]
[null, null]
[true, true]
*/
