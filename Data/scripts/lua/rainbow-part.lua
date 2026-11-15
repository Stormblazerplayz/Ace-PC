--[[
	RAINBOW PART
	Makes a part smoothly change through all the colors of the rainbow.

	HOW TO USE:
	1. Make a Part (Neon material looks awesome with this).
	2. Insert a Script into the Part and paste this in.
]]

local part = script.Parent

-- SETTINGS
local SECONDS_PER_RAINBOW = 5 -- how long one full trip through the colors takes
local UPDATE_EVERY = 0.05 -- seconds between color changes (smaller = smoother)

local hue = 0 -- hue is the "color wheel" position, from 0 to 1

while true do
	local deltaTime = task.wait(UPDATE_EVERY)
	hue = (hue + deltaTime / SECONDS_PER_RAINBOW) % 1
	-- fromHSV(hue, saturation, brightness): 1, 1 = bright and colorful
	part.Color = Color3.fromHSV(hue, 1, 1)
end
