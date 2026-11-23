local x = 42
local a = {}
for i = 1, 200000 do x = (x * 16807) % 2147483647; a[i] = x end
table.sort(a, function(p, q) return p < q end)
local check = 0
for i = 1, #a, 1000 do check = (check + a[i]) % 1000000007 end
print(check)
