#!/bin/bash

instance_name=asec-func
# instance_name=secureupload-func

func azure functionapp publish $instance_name
func azure functionapp logstream $instance_name