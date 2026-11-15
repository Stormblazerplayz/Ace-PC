--[[
	GIVE TOOL PAD
	Step on it to get a tool (like a sword or a flashlight) - only one copy each.

	HOW TO USE:
	1. Put the Tool you want to give in ServerStorage (for example a Tool named Sword).
	2. Make a Part (Anchored) on the ground.
	3. Insert a Script into the Part and paste this in.
	4. Set TOOL_NAME to the tool's exact name.
]]

local Players = game:GetService("Players")
local ServerStorage = game:GetService("ServerStorage")

local pad = script.Parent

-- SETTINGS
local TOOL_NAME = "Sword" -- the Tool's name in ServerStorage
local KEEP_AFTER_DEATH = false -- true = they keep it forever after respawning

pad.Anchored = true

local toolTemplate = ServerStorage:WaitForChild(TOOL_NAME)
local busy = {} -- debounce per player

local function hasTool(player)
	local backpack = player:FindFirstChildOfClass("Backpack")
	local character = player.Character
	if backpack and backpack:FindFirstChild(TOOL_NAME) then
		return true
	end
	if character and character:FindFirstChild(TOOL_NAME) then
		return true -- they are holding it right now
	end
	return false
end

pad.Touched:Connect(function(hit)
	local character = hit:FindFirstAncestorOfClass("Model")
	local player = character and Players:GetPlayerFromCharacter(character)
	if not player or busy[player] then
		return
	end
	local humanoid = character:FindFirstChildOfClass("Humanoid")
	if not humanoid or humanoid.Health <= 0 then
		return
	end

	busy[player] = true

	if not hasTool(player) then
		local backpack = player:FindFirstChildOfClass("Backpack")
		if backpack then
			toolTemplate:Clone().Parent = backpack
		end
		if KEEP_AFTER_DEATH then
			local starterGear = player:FindFirstChild("StarterGear")
			if starterGear and not starterGear:FindFirstChild(TOOL_NAME) then
				toolTemplate:Clone().Parent = starterGear
			end
		end
	end

	task.delay(1, function()
		busy[player] = nil
	end)
end)
