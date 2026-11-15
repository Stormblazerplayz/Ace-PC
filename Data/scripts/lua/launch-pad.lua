--[[
	LAUNCH PAD (shoots you forward AND up, like a cannon)
	The player flies in the direction the pad's FRONT is facing.

	HOW TO USE:
	1. Make a Part (Anchored). Rotate it so its Front face points where you want to fly.
	   (Tip: select the part and look for the "Front" in the Properties > Surface section,
	   or put a Decal on the Front face to see which way it is.)
	2. Insert a Script into the Part and paste this in.
]]

local Debris = game:GetService("Debris")

local pad = script.Parent

-- SETTINGS
local FORWARD_SPEED = 80 -- how fast you fly forward
local UP_SPEED = 70 -- how fast you fly up
local PUSH_TIME = 0.25 -- seconds the push lasts
local COOLDOWN = 1 -- seconds before the same person can launch again

pad.Anchored = true

local cooling = {} -- debounce list

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

	-- Flat forward direction of the pad (ignore any tilt) + some upward speed
	local forward = pad.CFrame.LookVector * Vector3.new(1, 0, 1)
	if forward.Magnitude > 0 then
		forward = forward.Unit
	end
	local launchVelocity = forward * FORWARD_SPEED + Vector3.new(0, UP_SPEED, 0)

	local attachment = Instance.new("Attachment")
	attachment.Name = "LaunchPadAttachment"
	attachment.Parent = rootPart

	local push = Instance.new("LinearVelocity")
	push.Name = "LaunchPadPush"
	push.Attachment0 = attachment
	push.RelativeTo = Enum.ActuatorRelativeTo.World
	push.VelocityConstraintMode = Enum.VelocityConstraintMode.Vector
	push.VectorVelocity = launchVelocity
	push.MaxForce = math.huge
	push.Parent = rootPart

	Debris:AddItem(push, PUSH_TIME)
	Debris:AddItem(attachment, PUSH_TIME)

	task.delay(COOLDOWN, function()
		cooling[character] = nil
	end)
end)
