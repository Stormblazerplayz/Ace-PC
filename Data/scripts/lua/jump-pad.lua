--[[
	JUMP PAD
	Step on it and you get launched high into the air!

	HOW TO USE:
	1. Make a flat Part (Anchored) on the ground.
	2. Insert a Script into the Part and paste this in.
	3. Change JUMP_SPEED to jump higher or lower.

	It uses a LinearVelocity (the modern replacement for the old BodyVelocity)
	for a split second to push the player up.
]]

local Debris = game:GetService("Debris")

local pad = script.Parent

-- SETTINGS
local JUMP_SPEED = 100 -- upward speed. 100 is about 25 studs high, 150 is about 55 studs
local PUSH_TIME = 0.2 -- how long the push lasts (seconds)
local COOLDOWN = 0.6 -- seconds before the same person can bounce again

pad.Anchored = true

local cooling = {} -- characters that just bounced (our debounce)

pad.Touched:Connect(function(hit)
	local character = hit:FindFirstAncestorOfClass("Model")
	if not character or cooling[character] then
		return
	end

	local humanoid = character:FindFirstChildOfClass("Humanoid")
	local rootPart = character:FindFirstChild("HumanoidRootPart")
	if not humanoid or humanoid.Health <= 0 or not rootPart then
		return
	end

	cooling[character] = true

	-- An Attachment is the "handle" the push grabs onto
	local attachment = Instance.new("Attachment")
	attachment.Name = "JumpPadAttachment"
	attachment.Parent = rootPart

	-- Push ONLY up/down (Line mode), so you can still steer left and right
	local push = Instance.new("LinearVelocity")
	push.Name = "JumpPadPush"
	push.Attachment0 = attachment
	push.RelativeTo = Enum.ActuatorRelativeTo.World
	push.VelocityConstraintMode = Enum.VelocityConstraintMode.Line
	push.LineDirection = Vector3.new(0, 1, 0)
	push.LineVelocity = JUMP_SPEED
	push.MaxForce = math.huge
	push.Parent = rootPart

	-- Remove the push after a moment so gravity brings you back down
	Debris:AddItem(push, PUSH_TIME)
	Debris:AddItem(attachment, PUSH_TIME)

	task.delay(COOLDOWN, function()
		cooling[character] = nil
	end)
end)
