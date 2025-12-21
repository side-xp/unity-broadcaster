# Installation

## Option 1: Using the Package Manager

1. In your *Unity* project, go to `Window > Package Management > Package Manager` (or `Window > Package Manager` for *Unity 6.0-*)
2. Click on the *+* icon in the top-left corner, and select *Install package from Git URL...*
3. In the text field, enter the URL to this package's repository (including the `*.git` extensions), and click *Install*
4. Wait for Unity to get the files, and you're ready to go!

> Tip: if you need to use a specific version of this package for your project, add `#<tag-name>` to the URL before clicking on the *Install* button.

## Option 2: Extracting archive manually

1. Go to this project's `/releases` list
2. Download the ZIP file archive of your desired version
3. Extract the content of that archive into the `Packages/` folder of your Unity project
4. Wait for Unity to reload the solution, and you're ready to go!

> Tip: to avoid any path issue, make sure the folder that contains the package content has the same name as the `name` property defined in its `package.json` file.