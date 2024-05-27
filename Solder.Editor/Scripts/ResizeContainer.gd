extends PanelContainer

func _ready():
	pass # Replace with function body.

## Force Size to be Window Size
func _process(delta):
	size = DisplayServer.window_get_size()
	pass
