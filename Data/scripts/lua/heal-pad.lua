--[[
	HEAL PAD
	Step on it to get health back. It flashes green when it heals someone.

	HOW TO USE:
	1. Make a flat Part (Anchored).
	2. Insert a Script into the Part and paste this in.
]]

local Players = game:GetService("Players")

local pad = script.Parent

-- SETTINGS
local HEAL_AMOUNT = 25 -- health given each time
local COOLDOWN = 2 -- seconds before the same person can heal again
local ONLY_PLAYERS = true -- false = NPCs can heal here too

pad.Anchored = true

local normalColor = pad.Color
local lastHeal = {} -- [humanoid] = time of last heal (our debounce)

pad.Touched:Connect(function(hit)
	local character = hit:FindFirstAncestorOfClass("Model")
	if not character then
		return
	end
	if ONLY_PLAYERS and not Players:GetPlayerFromCharacter(character) then
		return
	end

	local humanoid = character:FindFirstChildOfClass("Humanoid")
	if not humanoid or humanoid.Health <= 0 or humanoid.Health >= humanoid.MaxHealth then
		return -- dead or already full health
	end

	local now = os.clock()
	if lastHeal[humanoid] and now - lastHeal[humanoid] < COOLDOWN then
		return
	end
	lastHeal[humanoid] = now

	humanoid.Health = math.min(humanoid.MaxHealth, humanoid.Health + HEAL_AMOUNT)

	-- Little green flash so you know it worked
	pad.Color = Color3.fromRGB(80, 255, 120)
	task.delay(0.3, function()
		pad.Color = normalColor
	end)

	-- Forget this humanoid when it is removed (keeps memory clean)
	if not humanoid:GetAttribute("HealPadWatched") then
		humanoid:SetAttribute("HealPadWatched", true)
		humanoid.Destroying:Connect(function()
			lastHeal[humanoid] = nil
		end)
	end
end)
