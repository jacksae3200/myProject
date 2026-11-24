using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;

// Uses the installed Shader Graph editor API through reflection because its node types are internal.
// The resulting asset contains only standard, editable Shader Graph nodes, with no custom HLSL dependency.
public static class BuildScopeLensGraph
{
    private const string GRAPH_PATH = "Assets/sniper/ScopeLens/ScopeLensGraph.shadergraph";
    private const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static object _graph, _group;
    private static int _nodes, _edges;
    private sealed class Pin { public object Node; public int Slot; public Pin(object node,int slot){Node=node;Slot=slot;} }
    private static Type T(string name)
    {
        foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
            foreach(var prefix in new[]{"UnityEditor.ShaderGraph.","UnityEditor.ShaderGraph.Internal.","UnityEditor.ShaderGraph.Serialization.","UnityEditor.Rendering.Universal.ShaderGraph."})
            {var type=assembly.GetType(prefix+name);if(type!=null)return type;}
        throw new Exception("Shader Graph type missing: "+name);
    }
    private static object New(string type)=>Activator.CreateInstance(T(type),true);
    private static object Get(object obj,string name)
    {
        for(Type t=obj.GetType();t!=null;t=t.BaseType)
        {var p=t.GetProperty(name,FLAGS|BindingFlags.DeclaredOnly);if(p!=null)return p.GetValue(obj);var f=t.GetField(name,FLAGS|BindingFlags.DeclaredOnly);if(f!=null)return f.GetValue(obj);}
        throw new Exception(obj.GetType().Name+"."+name+" missing");
    }
    private static void Set(object obj,string name,object value)
    {
        for(Type t=obj.GetType();t!=null;t=t.BaseType)
        {var p=t.GetProperty(name,FLAGS|BindingFlags.DeclaredOnly);if(p!=null&&p.CanWrite){p.SetValue(obj,value);return;}var f=t.GetField(name,FLAGS|BindingFlags.DeclaredOnly);if(f!=null){f.SetValue(obj,value);return;}}
        throw new Exception(obj.GetType().Name+"."+name+" missing");
    }
    private static void EnumSet(object obj,string name,string value)
    {object old=Get(obj,name);Set(obj,name,Enum.Parse(old.GetType(),value));}
    private static object Call(object obj,string name,params object[] args)
    {
        Type type=obj as Type??obj.GetType();
        var methods=type.GetMethods(FLAGS).Where(m=>m.Name==name&&!m.IsGenericMethodDefinition);
        foreach(var m in methods)
        {
            var ps=m.GetParameters();if(args.Length>ps.Length)continue;
            bool fit=true;for(int i=0;i<ps.Length;i++)
            {if(i>=args.Length){if(!ps[i].IsOptional)fit=false;}else if(args[i]!=null&&!ps[i].ParameterType.IsInstanceOfType(args[i]))fit=false;}
            if(!fit)continue;
            object[] values=new object[ps.Length];for(int i=0;i<ps.Length;i++)values[i]=i<args.Length?args[i]:ps[i].DefaultValue;
            try{return m.Invoke(obj is Type?null:obj,values);}catch(TargetInvocationException e){throw new Exception(type.Name+"."+name+": "+e.InnerException,e.InnerException);}
        }
        throw new Exception("Method not found: "+type.Name+"."+name+"("+args.Length+")");
    }
    private static void Group(string title,float x,float y)
    {
        _group=Activator.CreateInstance(T("GroupData"),new object[]{title,new Vector2(x,y)});
        Call(_graph,"CreateGroup",_group);
    }
    private static object Node(string type,string label,float x,float y)
    {
        object node=New(type);Call(_graph,"AddNode",node);
        Set(node,"name",label);
        object state=Get(node,"drawState");Set(state,"position",new Rect(x,y,190,150));Set(node,"drawState",state);
        if(_group!=null)Call(_graph,"SetGroup",node,_group);
        _nodes++;return node;
    }
    private static void Edge(Pin from,object to,int slot)
    {
        object edge=Call(_graph,"Connect",Call(from.Node,"GetSlotReference",from.Slot),Call(to,"GetSlotReference",slot));
        if(edge==null)throw new Exception("Connection refused: "+Get(from.Node,"name")+" -> "+Get(to,"name"));_edges++;
    }
    private static Pin C(float value,float x,float y)
    {object node=Node("Vector1Node",value.ToString(System.Globalization.CultureInfo.InvariantCulture),x,y);Set(node,"m_Value",value);
        var find=node.GetType().GetMethods(FLAGS).First(m=>m.Name=="FindSlot"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==1);
        object slot=find.MakeGenericMethod(T("MaterialSlot")).Invoke(node,new object[]{1});Set(slot,"value",value);return new Pin(node,0);}
    private static Pin Binary(string type,Pin a,Pin b,string label,float x,float y)
    {object n=Node(type,label,x,y);Edge(a,n,0);Edge(b,n,1);return new Pin(n,2);}
    private static Pin Unary(string type,Pin a,string label,float x,float y)
    {object n=Node(type,label,x,y);Edge(a,n,0);return new Pin(n,1);}
    private static Pin Swizzle(Pin a,string mask,float x,float y)
    {object n=Node("SwizzleNode",mask,x,y);Set(n,"maskInput",mask);Edge(a,n,0);return new Pin(n,1);}
    private static object Property(string type,string label,string reference,object value=null)
    {
        object p=New(type);Set(p,"displayName",label);Set(p,"overrideReferenceName",reference);Set(p,"generatePropertyBlock",true);
        if(value!=null)Set(p,"value",value);Call(_graph,"AddGraphInput",p);return p;
    }
    private static Pin PropertyPin(object prop,float x,float y)
    {object n=Node("PropertyNode",(string)Get(prop,"displayName"),x,y);Set(n,"property",prop);return new Pin(n,0);}
    private static object Slider(string label,string reference,float value,float min,float max)
    {object p=Property("Vector1ShaderProperty",label,reference,value);EnumSet(p,"floatType","Slider");Set(p,"rangeValues",new Vector2(min,max));return p;}
    private static object Block(string stage,string name)
    {var type=T("BlockFields").GetNestedType(stage,BindingFlags.Public|BindingFlags.NonPublic);return type.GetField(name,FLAGS).GetValue(null);}
    private static object FindBlock(string name)
    {
        object context=Get(_graph,"fragmentContext");
        foreach(object wrapper in (IEnumerable)Get(context,"blocks"))
        {object node=Get(wrapper,"value");if(((string)Get(node,"name")).EndsWith("."+name))return node;}
        throw new Exception("Output block missing: "+name);
    }
    public static string Execute() => Build(false);
    public static string Rebuild() => Build(true);
    private static string Build(bool overwrite)
    {
        if(File.Exists(GRAPH_PATH) && !overwrite)throw new InvalidOperationException("Graph already exists; preserve edits before rebuilding.");
        _nodes=_edges=0;_group=null;_graph=New("GraphData");Set(_graph,"path","CpcPrototype");Set(_graph,"isSubGraph",false);
        Call(_graph,"SetGraphDefaultPrecision",Enum.Parse(T("GraphPrecision"),"Single"));
        object target=New("UniversalTarget");Call(target,"TrySetActiveSubTarget",T("UniversalUnlitSubTarget"));
        EnumSet(target,"surfaceType","Transparent");EnumSet(target,"alphaMode","Alpha");EnumSet(target,"renderFace","Both");
        EnumSet(target,"zWriteControl","ForceDisabled");Set(target,"alphaClip",true);Set(target,"castShadows",false);Set(target,"receiveShadows",false);Set(target,"allowMaterialOverride",false);
        object unlit=Get(target,"activeSubTarget");Set(unlit,"defaultDecalBlending",false);Set(unlit,"defaultSSAO",false);
        Array targets=Array.CreateInstance(T("Target"),1);targets.SetValue(target,0);
        object[] descriptors={Block("VertexDescription","Position"),Block("VertexDescription","Normal"),Block("VertexDescription","Tangent"),Block("SurfaceDescription","BaseColor"),Block("SurfaceDescription","Alpha"),Block("SurfaceDescription","AlphaClipThreshold")};
        Array blocks=Array.CreateInstance(T("BlockFieldDescriptor"),descriptors.Length);for(int i=0;i<descriptors.Length;i++)blocks.SetValue(descriptors[i],i);
        Call(_graph,"AddContexts");
        Call(_graph,"InitializeOutputs",targets,blocks);
        var main=Property("Texture2DShaderProperty","Camera Render Texture","_MainTex");EnumSet(main,"defaultType","Black");
        var bump=Property("Texture2DShaderProperty","Circular Lens Normal","_BumpMap");EnumSet(bump,"defaultType","NormalMap");
        var distortion=Slider("Lens Curvature","_Distortion",.09f,-.3f,.3f);
        var zoom=Slider("Image Zoom","_Zoom",1.15f,1f,4f);
        var softness=Slider("Edge Softness","_EdgeSoftness",.025f,.001f,.15f);
        var vignette=Slider("Edge Darkening","_Vignette",.3f,0f,1f);
        var tint=Property("ColorShaderProperty","Glass Tint","_Tint",new Color(.94f,.98f,1f,1f));
        object category=New("CategoryData");Set(category,"name","Lens Controls");Call(_graph,"AddCategory",category);
        foreach(object property in new[]{main,bump,distortion,zoom,softness,vignette,tint})Call(category,"InsertItemIntoCategory",property);
        Set(Get(_graph,"vertexContext"),"position",new Vector2(1750f,300f));
        Set(Get(_graph,"fragmentContext"),"position",new Vector2(1750f,700f));
        Group("01 UV radius: length(UV * 2 - 1)",-1800,-900);
        var uvNode=Node("UVNode","UV0",-1750,-850);var uv=Swizzle(new Pin(uvNode,0),"xy",-1500,-850);
        var centered=Binary("SubtractNode",Binary("MultiplyNode",uv,C(2,-1500,-620),"UV * 2",-1240,-850),C(1,-1240,-620),"Centered UV",-990,-850);
        var radius=Unary("LengthNode",centered,"Lens radius",-740,-850);
        Group("02 Circular mask + fwidth antialiasing",-1800,-300);
        var dx=Unary("AbsoluteNode",Unary("DDXNode",radius,"ddx(radius)",-1750,-250),"abs(ddx)",-1510,-250);
        var dy=Unary("AbsoluteNode",Unary("DDYNode",radius,"ddy(radius)",-1750,0),"abs(ddy)",-1510,0);
        var width=Binary("AddNode",dx,dy,"fwidth(radius)",-1260,-230);
        var feather=Binary("MaximumNode",PropertyPin(softness,-1510,250),width,"max(softness, fwidth)",-1000,-230);
        var edge=Unary("OneMinusNode",feather,"1 - feather",-740,-230);
        var smooth=Node("SmoothstepNode","Soft circular edge",-480,-230);Edge(edge,smooth,0);Edge(C(1,-740,40),smooth,1);Edge(radius,smooth,2);
        var mask=Unary("OneMinusNode",new Pin(smooth,3),"Circular mask",-220,-230);
        Group("03 Normal-map curvature + zoom",-1800,700);
        var normal=Node("SampleTexture2DNode","Unpack tangent-space lens normal",-1490,750);EnumSet(normal,"textureType","Normal");Set(normal,"enableGlobalMipBias",false);Edge(PropertyPin(bump,-1750,750),normal,1);Edge(uv,normal,2);
        var normalXY=Swizzle(new Pin(normal,0),"xy",-1230,750);
        var r2=Binary("MultiplyNode",radius,radius,"radius squared",-1230,1020);
        var distortionOffset=Binary("MultiplyNode",Binary("MultiplyNode",normalXY,PropertyPin(distortion,-1230,1280),"normal.xy * curvature",-970,750),r2,"Radial lens distortion",-710,750);
        var uvOffset=Binary("SubtractNode",uv,C(.5f,-1750,1540),"UV - 0.5",-1490,1540);
        var zoomUV=Binary("AddNode",Binary("DivideNode",uvOffset,PropertyPin(zoom,-1490,1800),"Centered UV / zoom",-1230,1540),C(.5f,-1230,1800),"Zoom around center",-970,1540);
        var sampleUV=Unary("SaturateNode",Binary("AddNode",zoomUV,distortionOffset,"Zoom + curvature",-450,750),"Clamp camera UV",-190,750);
        Group("04 Camera image, tint, vignette and alpha",200,650);
        var image=Node("SampleTexture2DNode","Scope camera image",520,700);Set(image,"enableGlobalMipBias",false);Edge(PropertyPin(main,250,700),image,1);Edge(sampleUV,image,2);
        var rgb=Swizzle(new Pin(image,0),"rgb",780,700);
        var tintPin=PropertyPin(tint,250,1010);var tintRGB=Swizzle(tintPin,"rgb",520,1010);var tintAlpha=Swizzle(tintPin,"a",520,1280);
        var r4=Binary("PowerNode",radius,C(4,250,1560),"radius power 4",520,1560);
        var dark=Unary("OneMinusNode",Binary("MultiplyNode",PropertyPin(vignette,520,1800),r4,"Edge darkening",780,1560),"Vignette factor",1040,1560);
        var color=Binary("MultiplyNode",rgb,Binary("MultiplyNode",tintRGB,dark,"Tint * vignette",1040,1010),"Final lens RGB",1300,700);
        var alpha=Binary("MultiplyNode",mask,tintAlpha,"Mask * tint alpha",1040,1280);
        var threshold=Binary("MultiplyNode",tintAlpha,C(.001f,780,1800),"Mask clip threshold scaled by tint alpha",1300,1560);
        Edge(color,FindBlock("BaseColor"),0);Edge(alpha,FindBlock("Alpha"),0);Edge(threshold,FindBlock("AlphaClipThreshold"),0);
        Call(_graph,"ValidateGraph");
        string json=(string)Call(T("MultiJson"),"Serialize",_graph);
        File.WriteAllText(GRAPH_PATH,json);AssetDatabase.ImportAsset(GRAPH_PATH,ImportAssetOptions.ForceSynchronousImport);
        Shader shader=AssetDatabase.LoadAssetAtPath<Shader>(GRAPH_PATH);
        if(shader==null)throw new Exception("Shader Graph import failed.");
        if(ShaderUtil.ShaderHasError(shader))throw new Exception(string.Join("\n",ShaderUtil.GetShaderMessages(shader).Select(m=>m.message)));
        Selection.activeObject=shader;
        return $"Created {GRAPH_PATH}: {_nodes} standard nodes, {_edges} edges; shader={shader.name}; original shader untouched. Material replacement follows render comparison.";
    }
}
