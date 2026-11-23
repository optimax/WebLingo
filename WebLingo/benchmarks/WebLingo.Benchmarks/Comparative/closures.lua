local function counter(start)
  local n = start
  return function() n = n + 1; return n end
end
local total = 0
for i = 0, 1999 do
  local next = counter(i)
  for j = 1, 1000 do total = total + next() end
end
print(total)
