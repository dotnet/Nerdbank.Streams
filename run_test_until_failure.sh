#!/bin/bash

dotnet build
status=$?
until [[ $status -ne 0 ]]
do
    dotnet test
    status=$?
done
