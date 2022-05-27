﻿
#r "nuget: FParsec, 1.1.1"

#load "../helper.fs"
#load "../text.fs"
#load "../parsing.fs"
#load "../typing.fs"
#load "../codeGen.FSharp.fs"

open System.IO
open Trulla.Internal.Parsing
open Trulla.Internal.Typing
open Trulla.Internal.CodeGen.FSharp

let saveFile renderRes =
    let printLines() = printfn "---------------------------------"

    do printLines()
    match renderRes with
    | Ok res -> File.WriteAllText(Path.Combine(__SOURCE_DIRECTORY__, "output.fsx"), res)
    | Error err -> failwithf "ERROR: %A" err
    do printLines()




"""
Hello
{{if customer.isActive}}
ACTIVE{{end}}
{{for order in customer.orders}}
Order ID: {{order.id}} (yyyy){{end}}
xxxxxxx
{{if customer.isActive}}ANOTHER-IF{{end}}
"""
|> render
|> saveFile



// TODO: Testcase
////"""
////Hello
////{{if customer.isActive}}
////ACTIVE{{end}}
////{{for order in customer.orders}}
////Order ID: {{order.id}}yyyy{{end}}
////xxxxxxx
////{{if customer.isActive}}ANOTHER-IF{{end}}
////"""
////|> parseTemplate
////|> Result.bind buildTree
////|> Result.map (fun tree ->
////    let rec printTree indent tree =
////        for x in tree do
////            match x with
////            | Text txt ->
////                printfn $"""{String.replicate indent "    "}TEXT ({txt.Replace("\n", "")})"""
////            | Hole hole -> 
////                printfn $"""{String.replicate indent "    "}HOLE ({memberExpToIdent hole})"""
////            | For (_,_,body) ->
////                printfn $"""{String.replicate indent "    "}FOR"""
////                printTree (indent + 1) body
////            | If (_,body) ->
////                printfn $"""{String.replicate indent "    "}IF"""
////                printTree (indent + 1) body
////    printTree 0 tree
////)
//////|> render
//////|> saveFile

