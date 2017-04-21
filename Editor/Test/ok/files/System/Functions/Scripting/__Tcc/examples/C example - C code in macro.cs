 This macro shows how to place C code and QM calling code in same macro without using a string.

__Tcc x.Compile("" "func2") ;;when first argument is "", gets C code from this macro, below the #ret line
out call(x.f 4)

#ret ;;QM does not compile macro below #ret as QM code

int func1(int x)
{
return x*x;
}

int func2(int x)
{
return func1(x);
}