--[[
	SWORD (simple tool)
	Click to swing. Anyone the blade touches during the swing takes damage.
	It also "tags" who hit them, so the kills-leaderboard script can give you the kill.

	HOW TO USE:
	1. In StarterPack, insert a Tool (name it Sword).
	2. Put a Part inside the Tool and name it exactly: Handle
	   (Make it long and thin like a blade. Turn OFF Anchored and CanCollide.)
	3. Insert a Script inside the Tool (NOT inside the Handle) and paste this in.
	Optional: put a Sound named SwingSound inside the Handle.
]]

local Players = game:GetService("Players")
local Debris = game:GetService("Debris")

local tool = script.Parent
local handle = tool:WaitForChild("Handle")

-- SETTINGS
local DAMAGE = 25 -- damage per hit
local SWING_TIME = 0.4 -- seconds the blade can hurt people after you click
local COOLDOWN = 0.7 -- seconds between swings (must be bigger than SWING_TIME)
local FRIENDLY_FIRE = false -- false = you can't hurt people on your own team

local swinging = false
local onCooldown = false -- debounce for clicking
local hitThisSwing = {} -- each person only gets hit once per swing

-- Who is holding the sword?
local function getOwner()
	local character = tool.Parent
	if character and character:IsA("Model") then
		return character, Players:GetPlayerFromCharacter(character)
	end
	return nil, nil
end

-- Leave a "creator" tag so the leaderboard knows who did the damage
local function tagHumanoid(humanoid, attacker)
	for _, child in humanoid:GetChildren() do
		if child.Name == "creator" and child:IsA("ObjectValue") then
			child:Destroy()
		end
	end
	local tag = Instance.new("ObjectValue")
	tag.Name = "creator"
	tag.Value = attacker
	tag.Parent = humanoid
	Debris:AddItem(tag, 3)
end

handle.Touched:Connect(function(hit)
	if not swinging then
		return
	end

	local myCharacter, myPlayer = getOwner()
	if not myCharacter then
		return
	end

	local otherCharacter = hit:FindFirstAncestorOfClass("Model")
	if not otherCharacter or otherCharacter == myCharacter then
		return -- don't hit yourself!
	end

	local humanoid = otherCharacter:FindFirstChildOfClass("Humanoid")
	if not humanoid or humanoid.Health <= 0 or hitThisSwing[humanoid] then
		return
	end

	-- Team check
	local otherPlayer = Players:GetPlayerFromCharacter(otherCharacter)
	if not FRIENDLY_FIRE and myPlayer and otherPlayer then
		if not myPlayer.Neutral and myPlayer.Team ~= nil and myPlayer.Team == otherPlayer.Team then
			return
		end
	end

	hitThisSwing[humanoid] = true
	if myPlayer then
		tagHumanoid(humanoid, myPlayer)
	end
	humanoid:TakeDamage(DAMAGE)
end)

tool.Activated:Connect(function()
	if onCooldown then
		return
	end
	onCooldown = true
	swinging = true
	table.clear(hitThisSwing)

	-- This tells Roblox's default Animate script to play the sword slash animation
	local slash = Instance.new("StringValue")
	slash.Name = "toolanim"
	slash.Value = "Slash"
	slash.Parent = tool
	Debris:AddItem(slash, 1)

	local sound = handle:FindFirstChild("SwingSound")
	if sound and sound:IsA("Sound") then
		sound:Play()
	end

	task.wait(SWING_TIME)
	swinging = false
	task.wait(math.max(0, COOLDOWN - SWING_TIME))
	onCooldown = false
end)

tool.Unequipped:Connect(function()
	swinging = false
end)
