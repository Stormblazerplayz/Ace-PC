--[[
	DAMAGE ZONE (poison gas, fire, radiation)
	Anyone standing inside this part slowly loses health over time.

	HOW TO USE:
	1. Make a Part that covers the dangerous area (it can be big, like a cloud).
	2. Insert a Script into the Part and paste this in.
	The script makes the part see-through and walk-through for you.
]]

local Players = game:GetService("Players")

local zone = script.Parent

-- SETTINGS
local DAMAGE = 10 -- health lost each time
local DAMAGE_EVERY = 1 -- seconds between hits (this is the cooldown)
local ONLY_PLAYERS = false -- true = NPCs don't get hurt
local ZONE_COLOR = Color3.fromRGB(120, 255, 60) -- green poison

zone.Anchored = true
zone.CanCollide = false
zone.Transparency = 0.7
zone.Color = ZONE_COLOR

local overlapParams = OverlapParams.new()
overlapParams.FilterType = Enum.RaycastFilterType.Exclude
overlapParams.FilterDescendantsInstances = { zone }

while true do
	-- Find every humanoid that has at least one body part inside the zone
	local hurtThisTime = {} -- so each person only gets hurt once per tick
	for _, part in workspace:GetPartsInPart(zone, overlapParams) do
		local character = part:FindFirstAncestorOfClass("Model")
		local humanoid = character and character:FindFirstChildOfClass("Humanoid")
		if humanoid and humanoid.Health > 0 and not hurtThisTime[humanoid] then
			if not ONLY_PLAYERS or Players:GetPlayerFromCharacter(character) then
				hurtThisTime[humanoid] = true
				humanoid:TakeDamage(DAMAGE) -- TakeDamage respects ForceFields (spawn protection)
			end
		end
	end
	task.wait(DAMAGE_EVERY)
end
