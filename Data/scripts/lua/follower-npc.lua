--[[
	FOLLOWER NPC (follows / chases the nearest player)
	The NPC walks toward whichever player is closest.
	Set TOUCH_DAMAGE above 0 to turn it into a zombie that hurts you!

	HOW TO USE:
	1. Get an NPC: Avatar tab > Rig Builder > pick a rig (R15 or R6).
	   It must have a Humanoid and a HumanoidRootPart. Make sure NO parts are Anchored.
	2. Insert a Script inside the NPC Model and paste this in.
]]

local Players = game:GetService("Players")

local npc = script.Parent
local humanoid = npc:WaitForChild("Humanoid")
local rootPart = npc:WaitForChild("HumanoidRootPart")

-- SETTINGS
local FOLLOW_RANGE = 80 -- how far away (studs) it can notice players
local STOP_DISTANCE = 5 -- stops this close to you (so it doesn't push you around)
local THINK_EVERY = 0.25 -- seconds between path updates
local WALK_SPEED = 14 -- how fast it walks (players walk at 16)
local TOUCH_DAMAGE = 0 -- 0 = friendly. Try 10 for a zombie!
local DAMAGE_COOLDOWN = 1 -- seconds between bites

humanoid.WalkSpeed = WALK_SPEED

-- Let the server control the NPC's physics, so it moves smoothly
pcall(function()
	rootPart:SetNetworkOwner(nil)
end)

local function findNearestPlayer()
	local nearestRoot = nil
	local nearestDistance = FOLLOW_RANGE
	for _, player in Players:GetPlayers() do
		local character = player.Character
		local theirHumanoid = character and character:FindFirstChildOfClass("Humanoid")
		local theirRoot = character and character:FindFirstChild("HumanoidRootPart")
		if theirHumanoid and theirHumanoid.Health > 0 and theirRoot then
			local distance = (theirRoot.Position - rootPart.Position).Magnitude
			if distance < nearestDistance then
				nearestRoot = theirRoot
				nearestDistance = distance
			end
		end
	end
	return nearestRoot, nearestDistance
end

-- Zombie bite (only if TOUCH_DAMAGE is more than 0)
if TOUCH_DAMAGE > 0 then
	local lastBite = {}
	for _, part in npc:GetDescendants() do
		if part:IsA("BasePart") then
			part.Touched:Connect(function(hit)
				local character = hit:FindFirstAncestorOfClass("Model")
				if not character or not Players:GetPlayerFromCharacter(character) then
					return
				end
				local theirHumanoid = character:FindFirstChildOfClass("Humanoid")
				if not theirHumanoid or theirHumanoid.Health <= 0 or humanoid.Health <= 0 then
					return
				end
				local now = os.clock()
				if lastBite[theirHumanoid] and now - lastBite[theirHumanoid] < DAMAGE_COOLDOWN then
					return -- debounce
				end
				lastBite[theirHumanoid] = now
				theirHumanoid:TakeDamage(TOUCH_DAMAGE)
			end)
		end
	end
end

-- The thinking loop: find someone, walk to them, repeat
while npc.Parent and humanoid.Health > 0 do
	local targetRoot, distance = findNearestPlayer()
	if targetRoot then
		if distance > STOP_DISTANCE then
			humanoid:MoveTo(targetRoot.Position)
			-- If the player is higher up, try jumping
			if targetRoot.Position.Y - rootPart.Position.Y > 3 then
				humanoid.Jump = true
			end
		else
			humanoid:MoveTo(rootPart.Position) -- close enough, stand still
		end
	end
	task.wait(THINK_EVERY)
end
