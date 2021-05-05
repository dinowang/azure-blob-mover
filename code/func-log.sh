#!/bin/bash

instance_name=asec-func
# instance_name=secureupload-func

func azure functionapp logstream $instance_name
