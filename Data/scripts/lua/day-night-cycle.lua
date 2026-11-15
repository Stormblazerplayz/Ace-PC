--[[
	DAY / NIGHT CYCLE
	Makes the sun go down and the moon come up, over and over.
	Bonus: lights in a "NightLights" folder turn on at night and off in the day.

	HOW TO USE:
	1. Put this Script in ServerScriptService.
	2. (Optional) Make a Folder in Workspace named NightLights and put lamp Parts in it.
	   Put a PointLight (or SpotLight/SurfaceLight) inside each lamp Part.
]]

local Lighting = game:GetService("Lighting")

-- SETTINGS
local MINUTES_PER_DAY = 10 -- real-life minutes for one full day (24 game hours)
local START_HOUR = 8 -- what time the game starts at (0-24). 8 = morning
local NIGHT_STARTS = 18 -- 6 PM
local NIGHT_ENDS = 6 -- 6 AM
local UPDATE_EVERY = 0.1 -- seconds between updates (smaller = smoother)

Lighting.ClockTime = START_HOUR

local hoursPerSecond = 24 / (MINUTES_PER_DAY * 60)
local lastIsNight = nil

local function updateLamps(isNight)
	local lampsFolder = workspace:FindFirstChild("NightLights")
	if not lampsFolder then
		return
	end
	for _, object in lampsFolder:GetDescendants() do
		if object:IsA("Light") then
			object.Enabled = isNight
		elseif object:IsA("BasePart") then
			object.Material = isNight and Enum.Material.Neon or Enum.Material.SmoothPlastic
		end
	end
end

while true do
	local deltaTime = task.wait(UPDATE_EVERY)
	Lighting.ClockTime = (Lighting.ClockTime + deltaTime * hoursPerSecond) % 24

	local hour = Lighting.ClockTime
	local isNight = hour >= NIGHT_STARTS or hour < NIGHT_ENDS

	-- Only change the lamps when day turns to night (or night to day)
	if isNight ~= lastIsNight then
		lastIsNight = isNight
		Lighting:SetAttribute("IsNight", isNight) -- other scripts can check this too
		updateLamps(isNight)
	end
end
