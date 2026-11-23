local m = {}
for i = 0, 199999 do m["k" .. tostring(i % 50000)] = i end
local hits = 0
for i = 0, 199999 do
  local k = "k" .. tostring(i)
  local v = m[k]
  if v ~= nil then hits = hits + v end
end
print(hits)
