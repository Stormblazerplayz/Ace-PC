--[[
	TWO-WAY TELEPORTER PADS
	Step on PadA and you go to PadB. Step on PadB and you go to PadA.

	HOW TO USE:
	1. Make a Model (or Folder) in Workspace, for example called "Teleporter".
	2. Put TWO Parts inside it and name them exactly: PadA and PadB
	3. Put this Script inside the same Model/Folder (next to the pads).
]]

local Players = game:GetService("Players")

local container = script.Parent
local padA = container:WaitForChild("PadA")
local padB = container:WaitForChild("PadB")

-- SETTINGS
local COOLDOWN = 2 -- seconds before the same person can teleport again
local HEIGHT_ABOVE_PAD = 4 -- how high above the pad you appear

local recentlyTeleported = {} -- remembers who just teleported (this is our debounce)

local function teleport(hit, destinationPad)
	local character = hit:FindFirstAncestorOfClass("Model")
	if not character then
		return
	end

	local humanoid = character:FindFirstChildOfClass("Humanoid")
	local rootPart = character:FindFirstChild("HumanoidRootPart")
	if not humanoid or humanoid.Health <= 0 or not rootPart then
		return
	end

	-- Only teleport players (delete these 3 lines if NPCs should teleport too)
	if not Players:GetPlayerFromCharacter(character) then
		return
	end

	if recentlyTeleported[character] then
		return
	end
	recentlyTeleported[character] = true

	-- Move the whole character to just above the other pad
	local topOfPad = destinationPad.Position + Vector3.new(0, destinationPad.Size.Y / 2 + HEIGHT_ABOVE_PAD, 0)
	character:PivotTo(CFrame.new(topOfPad))

	-- After the cooldown they can use a pad again
	task.delay(COOLDOWN, function()
		recentlyTeleported[character] = nil
	end)
end

padA.Touched:Connect(function(hit)
	teleport(hit, padB)
end)

padB.Touched:Connect(function(hit)
	teleport(hit, padA)
end)
